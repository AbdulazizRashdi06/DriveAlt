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
    int Limit = 12);

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

    public async Task<List<SearchHit>> SearchAsync(SearchQuery q, CancellationToken ct)
    {
        var ts = OrTsQuery(q.Text);
        Vector? vec = null;
        if (ai.Online && !string.IsNullOrWhiteSpace(q.Text))
        {
            try
            {
                var v = (await ai.EmbedAsync([q.Text], query: true, ct))[0];
                if (v is not null) vec = new Vector(v);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning(e, "Query embedding failed; keyword search only");
            }
        }

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

        if (ts is null && vec is null)
        {
            // No usable words (e.g. just filters): newest material first.
            var recent = await pages.OrderByDescending(p => p.File!.AcademicYear).ThenBy(p => p.File!.Title).ThenBy(p => p.Number)
                .Select(p => new { p.Id }).Take(Candidates).ToListAsync(ct);
            lists.Add((recent.Select(p => ("p:" + p.Id, p.Id, (Guid?)null)).ToList(), 1.0));
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
        if (qref.Success && scores.Keys.Any(k => k.StartsWith("r:")))
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

    private static IQueryable<Page> FilterPages(IQueryable<Page> pages, SearchQuery q)
    {
        pages = pages.Where(p => p.File!.Status == FileStatuses.Ready);
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
        regions = regions.Where(r => r.Label == RegionLabels.Question && r.Page!.File!.Status == FileStatuses.Ready);
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
