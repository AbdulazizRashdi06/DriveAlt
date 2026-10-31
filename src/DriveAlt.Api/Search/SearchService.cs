using System.Text.RegularExpressions;
using DriveAlt.Api.Ai;
using DriveAlt.Api.Data;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace DriveAlt.Api.Search;

public sealed record SearchQuery(
    string Text,
    IReadOnlyCollection<int>? CourseIds = null,
    IReadOnlyCollection<string>? Types = null,
    int? Year = null,
    string? AssessmentLabel = null,
    string? Tag = null,
    Guid? FileId = null,
    int Limit = 12,
    string? PageKind = null,
    bool QuestionsOnly = false,
    string? Meaning = null,
    IReadOnlyList<string>? Keywords = null)
{
    /// <summary>
    /// True when the chat model wrote the search itself: <see cref="Meaning"/> is embedded as written and
    /// <see cref="Keywords"/> are matched as given, with no stop words, boosts or topic extraction applied.
    /// </summary>
    public bool Authored => Meaning is not null || Keywords is not null;
}

public sealed record SearchHit(string Ref, Guid PageId, Guid? RegionId, double Score);

/// <summary>
/// Hybrid search: Postgres full-text rank + pgvector cosine distance over pages and question regions, merged with
/// reciprocal-rank fusion. Only processed files are searchable; answer text is never indexed (see IndexText).
/// </summary>
public sealed partial class SearchService(AppDb db, IMaterialAi ai, ILogger<SearchService> log)
{
    private const int Candidates = 60;
    private const double RrfK = 60;

    private static readonly HashSet<string> Stop = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "of", "on", "in", "for", "to", "and", "or", "with", "from", "about", "me", "my", "i", "we", "our",
        "give", "show", "find", "get", "want", "need", "some", "any", "all", "please", "where", "did", "do", "does", "is",
        "are", "was", "cover", "covered", "that", "this", "those", "these", "what", "which", "last", "year", "questions",
    };

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Token();

    [GeneratedRegex(@"\bq(?:uestion)?\s*\.?\s*(\d{1,2}[a-z]?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex QuestionRef();

    public static string? OrTsQuery(string text)
    {
        var words = Token().Matches(text.ToLowerInvariant()).Select(m => m.Value)
            .Where(w => !Stop.Contains(w) && (w.Length > 1 || char.IsDigit(w[0]))).Distinct().Take(16).ToList();
        return words.Count == 0 ? null : string.Join(" | ", words);
    }

    /// <summary>The model's keywords as a tsquery: each keyword (one word or a phrase) must match whole, any keyword may match.</summary>
    public static string? KeywordTsQuery(IEnumerable<string> keywords)
    {
        var groups = keywords.Select(k => Token().Matches(k.ToLowerInvariant()).Select(m => m.Value).ToList())
            .Where(t => t.Count > 0).Select(t => t.Count == 1 ? t[0] : "(" + string.Join(" & ", t) + ")").Distinct().Take(16).ToList();
        return groups.Count == 0 ? null : string.Join(" | ", groups);
    }

    public async Task<List<SearchHit>> SearchAsync(SearchQuery q, CancellationToken ct)
    {
        var ts = q.Authored ? (q.Keywords is null ? null : KeywordTsQuery(q.Keywords)) : OrTsQuery(q.Text);
        var embedText = q.Authored ? q.Meaning : q.Text;
        Vector? vec = null;
        if (ai.Online && !string.IsNullOrWhiteSpace(embedText))
        {
            try
            {
                var v = (await ai.EmbedAsync([embedText], query: true, ct))[0];
                if (v is not null) vec = new Vector(v);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning(e, "Query embedding failed; keyword search only");
            }
        }

        // The model's sentence only reaches the semantic search; if embeddings are unavailable, its words are the fallback.
        if (q.Authored && ts is null && vec is null && !string.IsNullOrWhiteSpace(q.Meaning)) ts = OrTsQuery(q.Meaning);

        var pages = FilterPages(db.Pages.AsNoTracking(), q);
        var regions = FilterRegions(db.Regions.AsNoTracking(), q);

        var lists = new List<(List<(string Ref, Guid PageId, Guid? RegionId)> Items, double Weight)>();

        if (ts is not null)
        {
            var pageFts = await pages
                .Where(p => p.Search!.Matches(EF.Functions.ToTsQuery("english", ts)))
                .OrderByDescending(p => p.Search!.RankCoverDensity(EF.Functions.ToTsQuery("english", ts)))
                .Select(p => new { p.Id }).Take(Candidates).ToListAsync(ct);
            lists.Add((pageFts.Select(p => ("p:" + p.Id, p.Id, (Guid?)null)).ToList(), 1.0));

            var regionFts = await regions
                .Where(r => r.Search!.Matches(EF.Functions.ToTsQuery("english", ts)))
                .OrderByDescending(r => r.Search!.RankCoverDensity(EF.Functions.ToTsQuery("english", ts)))
                .Select(r => new { r.Id, r.PageId }).Take(Candidates).ToListAsync(ct);
            lists.Add((regionFts.Select(r => ("r:" + r.Id, r.PageId, (Guid?)r.Id)).ToList(), 1.1));
        }

        if (vec is not null)
        {
            var pageVec = await pages.Where(p => p.Embedding != null)
                .OrderBy(p => p.Embedding!.CosineDistance(vec))
                .Select(p => new { p.Id }).Take(Candidates).ToListAsync(ct);
            lists.Add((pageVec.Select(p => ("p:" + p.Id, p.Id, (Guid?)null)).ToList(), 1.0));

            var regionVec = await regions.Where(r => r.Embedding != null)
                .OrderBy(r => r.Embedding!.CosineDistance(vec))
                .Select(r => new { r.Id, r.PageId }).Take(Candidates).ToListAsync(ct);
            lists.Add((regionVec.Select(r => ("r:" + r.Id, r.PageId, (Guid?)r.Id)).ToList(), 1.1));
        }

        if (ts is null && vec is null && (!q.Authored || (string.IsNullOrWhiteSpace(q.Meaning) && q.Keywords is not { Count: > 0 })))
        {
            // No search words (e.g. the model asked only for "Midterm 1, 2023" by filters): everything matching, newest first.
            var recent = await pages.OrderByDescending(p => p.File!.AcademicYear).ThenBy(p => p.File!.Title).ThenBy(p => p.Number)
                .Select(p => new { p.Id }).Take(Candidates).ToListAsync(ct);
            lists.Add((recent.Select(p => ("p:" + p.Id, p.Id, (Guid?)null)).ToList(), 1.0));
            var recentRegions = await regions.OrderByDescending(r => r.Page!.File!.AcademicYear)
                .ThenBy(r => r.Page!.File!.Title).ThenBy(r => r.Page!.Number).ThenBy(r => r.Y)
                .Select(r => new { r.Id, r.PageId }).Take(Candidates).ToListAsync(ct);
            lists.Add((recentRegions.Select(r => ("r:" + r.Id, r.PageId, (Guid?)r.Id)).ToList(), 1.1));
        }

        var scores = new Dictionary<string, (Guid PageId, Guid? RegionId, double Score)>();
        foreach (var (items, weight) in lists)
        {
            for (var i = 0; i < items.Count; i++)
            {
                var (r, pageId, regionId) = items[i];
                var add = weight / (RrfK + i + 1);
                scores[r] = scores.TryGetValue(r, out var cur) ? (pageId, regionId, cur.Score + add) : (pageId, regionId, add);
            }
        }

        // Boost exact question-number matches ("Q2", "question 3").
        var qref = QuestionRef().Match(q.Text);
        if (!q.Authored && qref.Success && scores.Keys.Any(k => k.StartsWith("r:")))
        {
            var num = qref.Groups[1].Value.ToLowerInvariant();
            var regionIds = scores.Values.Where(v => v.RegionId is not null).Select(v => v.RegionId!.Value).ToList();
            var matching = await db.Regions.AsNoTracking().Where(r => regionIds.Contains(r.Id) && r.QuestionNumber != null)
                .Select(r => new { r.Id, r.QuestionNumber }).ToListAsync(ct);
            foreach (var m in matching.Where(m => NormalizeQ(m.QuestionNumber!) == num || NormalizeQ(m.QuestionNumber!).StartsWith(num)))
            {
                var key = "r:" + m.Id;
                var cur = scores[key];
                scores[key] = cur with { Score = cur.Score + 0.05 };
            }
        }

        // Prefer the question crop over its whole page when both match.
        var drop = scores.Values.Where(v => v.RegionId is not null).Select(v => v.PageId).ToHashSet();

        return scores
            .Where(kv => !(kv.Value.RegionId is null && drop.Contains(kv.Value.PageId)))
            .OrderByDescending(kv => kv.Value.Score)
            .Take(q.Limit)
            .Select(kv => new SearchHit(kv.Key, kv.Value.PageId, kv.Value.RegionId, kv.Value.Score))
            .ToList();
    }

    private static string NormalizeQ(string s) => new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).SkipWhile(c => c == 'q').ToArray());

    /// <summary>One result per whole file, including held files whose originals/pages already exist.</summary>
    public async Task<List<SearchHit>> SearchFilesAsync(SearchQuery q, CancellationToken ct)
    {
        var files = db.Files.AsNoTracking().Where(f => FileStatuses.Visible.Contains(f.Status) && f.CourseId != null && f.Pages.Any());
        if (q.CourseIds is { Count: > 0 } courses) files = files.Where(f => courses.Contains(f.CourseId!.Value));
        if (q.Types is { Count: > 0 } types) files = files.Where(f => types.Contains(f.MaterialType));
        if (q.Year is { } year) files = files.Where(f => f.AcademicYear == year || f.Pages.Any(p => p.AcademicYear == year));
        if (q.FileId is { } fileId) files = files.Where(f => f.Id == fileId);
        if (!string.IsNullOrWhiteSpace(q.AssessmentLabel)) files = files.Where(f => EF.Functions.ILike(f.AssessmentLabel ?? f.Title, $"%{q.AssessmentLabel}%"));
        if (!string.IsNullOrWhiteSpace(q.Tag)) files = files.Where(f => f.Pages.Any(p => p.Tags.Contains(q.Tag)));

        var courseQuery = db.Courses.AsNoTracking();
        if (q.CourseIds is { Count: > 0 } selected) courseQuery = courseQuery.Where(c => selected.Contains(c.Id));
        var selectedCourses = await courseQuery.ToListAsync(ct);
        // The model's own search is used as written; Browse-style text still has its file words stripped.
        var topic = q.Authored ? string.Join(' ', q.Keywords ?? []) : FileTopic(q.Text, selectedCourses, q.AssessmentLabel);
        var hasTopic = q.Authored ? !string.IsNullOrWhiteSpace(q.Meaning) || topic.Length > 0 : topic.Length > 0;
        Dictionary<Guid, double>? scores = null;
        var bestPages = new Dictionary<Guid, Guid>();
        if (hasTopic)
        {
            var hits = await SearchAsync(q.Authored ? q with { Limit = Candidates * 2 } : q with { Text = topic, Limit = Candidates * 2 }, ct);
            var pageIds = hits.Select(h => h.PageId).Distinct().ToList();
            var pageFiles = await db.Pages.AsNoTracking().Where(p => pageIds.Contains(p.Id)).Select(p => new { p.Id, p.FileId }).ToDictionaryAsync(p => p.Id, ct);
            scores = hits.Where(h => pageFiles.ContainsKey(h.PageId)).GroupBy(h => pageFiles[h.PageId].FileId).ToDictionary(g => g.Key, g => g.Max(h => h.Score));
            bestPages = hits.Where(h => pageFiles.ContainsKey(h.PageId)).GroupBy(h => pageFiles[h.PageId].FileId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(h => h.Score).First().PageId);
            // An original can be found by its filename even while its AI/text indexing is held.
            foreach (var term in q.Authored ? (q.Keywords ?? []).Where(k => !string.IsNullOrWhiteSpace(k)).Take(8) : topic.Length > 0 ? [topic] : [])
            {
                var titleMatches = await files.Where(f => EF.Functions.ILike(f.Title, $"%{term.Trim()}%")).Select(f => f.Id).Take(Candidates).ToListAsync(ct);
                foreach (var id in titleMatches) scores[id] = Math.Max(scores.GetValueOrDefault(id), 1);
            }
            var ids = scores.Keys.ToArray();
            files = files.Where(f => ids.Contains(f.Id));
        }
        var candidates = await files.OrderByDescending(f => f.AcademicYear).ThenBy(f => f.Title).Take(scores is null ? q.Limit : scores.Count)
            .Select(f => new { f.Id, Cover = f.Pages.OrderBy(p => p.Number).First().Id }).ToListAsync(ct);
        var selectedPages = candidates.Select(f => bestPages.GetValueOrDefault(f.Id, f.Cover)).ToList();
        var numbers = await db.Pages.AsNoTracking().Where(p => selectedPages.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Number, ct);
        return candidates.OrderByDescending(f => scores?.GetValueOrDefault(f.Id) ?? 0).Take(q.Limit)
            .Select(f => new SearchHit(ResultRefs.File(f.Id, numbers[bestPages.GetValueOrDefault(f.Id, f.Cover)]),
                bestPages.GetValueOrDefault(f.Id, f.Cover), null, scores?.GetValueOrDefault(f.Id) ?? 0)).ToList();
    }

    private static readonly HashSet<string> FileBoilerplate = new(StringComparer.OrdinalIgnoreCase)
    {
        "ok", "okay", "hi", "hey", "full", "whole", "complete", "entire", "file", "files", "document", "documents", "pdf", "pdfs",
        "past", "paper", "papers", "pastpaper", "pastpapers", "exam", "exams", "midterm", "midterms", "final", "finals", "quiz", "quizzes", "exercise", "exercises",
        "slide", "slides", "lecture", "lectures", "notes", "lab", "labs", "download", "send", "gimme", "everything", "every", "only", "just",
        "can", "could", "would", "will", "you", "your", "us", "let", "have", "like", "look", "looking", "material", "materials", "course", "btw",
    };

    internal static string FileTopic(string text, IReadOnlyCollection<Course> courses, string? label = null)
    {
        var ignored = courses.SelectMany(c => c.Aliases.Append(c.Name).Append(c.Slug.Replace('-', ' ')))
            .Append(label ?? "").SelectMany(s => Token().Matches(s.ToLowerInvariant()).Select(m => m.Value)).ToHashSet();
        return string.Join(' ', Token().Matches(text.ToLowerInvariant()).Select(m => m.Value)
            .Where(w => !Stop.Contains(w) && !FileBoilerplate.Contains(w) && !ignored.Contains(w)
                && (!int.TryParse(w, out var number) || number is < 1900 or > 2100)).Distinct().Take(16));
    }

    private static IQueryable<Page> FilterPages(IQueryable<Page> pages, SearchQuery q)
    {
        pages = pages.Where(p => FileStatuses.Visible.Contains(p.File!.Status));
        if (q.PageKind is { } kind) pages = pages.Where(p => p.Kind == kind);
        if (q.QuestionsOnly) pages = pages.Where(p => p.Kind == PageKinds.Assessment
            && p.File!.MaterialType != MaterialTypes.Slides && p.File.MaterialType != MaterialTypes.Notes
            && !p.Regions.Any(r => r.Label == RegionLabels.Question));
        if (q.FileId is { } fid) pages = pages.Where(p => p.FileId == fid);
        if (q.CourseIds is { Count: > 0 } cs) pages = pages.Where(p => cs.Contains(p.File!.CourseId!.Value));
        if (q.Types is { Count: > 0 } ts) pages = pages.Where(p => ts.Contains(p.File!.MaterialType));
        if (q.Year is { } y) pages = pages.Where(p => p.File!.AcademicYear == y || p.AcademicYear == y);
        if (!string.IsNullOrWhiteSpace(q.AssessmentLabel))
            pages = pages.Where(p => EF.Functions.ILike(p.File!.AssessmentLabel ?? p.File.Title, $"%{q.AssessmentLabel}%"));
        if (!string.IsNullOrWhiteSpace(q.Tag)) pages = pages.Where(p => p.Tags.Contains(q.Tag));
        return pages;
    }

    private static IQueryable<Region> FilterRegions(IQueryable<Region> regions, SearchQuery q)
    {
        regions = regions.Where(r => r.Label == RegionLabels.Question && FileStatuses.Visible.Contains(r.Page!.File!.Status));
        // Specific page requests never return crops. Question searches exclude slide/notes crops too.
        if (q.PageKind is not null) return regions.Where(r => false);
        if (q.QuestionsOnly) regions = regions.Where(r => r.Page!.Kind != PageKinds.Slide && r.Page.Kind != PageKinds.Notes
            && r.Page.File!.MaterialType != MaterialTypes.Slides && r.Page.File.MaterialType != MaterialTypes.Notes);
        if (q.FileId is { } fid) regions = regions.Where(r => r.Page!.FileId == fid);
        if (q.CourseIds is { Count: > 0 } cs) regions = regions.Where(r => cs.Contains(r.Page!.File!.CourseId!.Value));
        if (q.Types is { Count: > 0 } ts) regions = regions.Where(r => ts.Contains(r.Page!.File!.MaterialType));
        if (q.Year is { } y) regions = regions.Where(r => r.Page!.File!.AcademicYear == y || r.Page.AcademicYear == y);
        if (!string.IsNullOrWhiteSpace(q.AssessmentLabel))
            regions = regions.Where(r => EF.Functions.ILike(r.Page!.File!.AssessmentLabel ?? r.Page.File.Title, $"%{q.AssessmentLabel}%"));
        if (!string.IsNullOrWhiteSpace(q.Tag)) regions = regions.Where(r => r.Page!.Tags.Contains(q.Tag));
        return regions;
    }
}
