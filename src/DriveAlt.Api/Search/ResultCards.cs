using DriveAlt.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DriveAlt.Api.Search;

public sealed record Box(float X, float Y, float W, float H);

/// <summary>
/// What the UI renders for a result: question crops or original pages with source metadata, never text.
/// </summary>
public sealed record ResultCard(
    string Ref,
    string Kind,
    string ImageUrl,
    string PageImageUrl,
    string ThumbUrl,
    Guid FileId,
    Guid PageId,
    Guid? RegionId,
    string FileTitle,
    string FileName,
    string SourceKind,
    string CourseSlug,
    string CourseName,
    string CourseColor,
    int Semester,
    string MaterialType,
    int? Year,
    string? AssessmentLabel,
    int PageNumber,
    int PageCount,
    string? QuestionNumber,
    string[] Tags,
    Box? Box,
    int Width,
    int Height,
    bool Saved,
    string? AnswerRef,
    string? AnswerImageUrl);

public static class ResultRefs
{
    public static bool TryParse(string r, out bool isRegion, out Guid id)
    {
        isRegion = r.StartsWith("r:");
        id = Guid.Empty;
        return (isRegion || r.StartsWith("p:")) && Guid.TryParse(r[2..], out id);
    }

    public static string Page(Guid id) => "p:" + id;
    public static string Region(Guid id) => "r:" + id;
}

public sealed class ResultCards(AppDb db)
{
    public static string Version(Page p) => (p.EditedAt ?? p.File?.UpdatedAt ?? DateTime.UnixEpoch).Ticks.ToString("x");

    private static string NormalizeQ(string? s) =>
        s is null ? "" : new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).SkipWhile(c => c == 'q').ToArray());

    /// <summary>
    /// The answer region that belongs to a question on the same page: same question number if both have one,
    /// else the first answer that starts inside the question box or below it and above the next question.
    /// </summary>
    public static Region? AnswerFor(Region question, IReadOnlyCollection<Region> pageRegions)
    {
        var answers = pageRegions.Where(r => r.Label == RegionLabels.Answer).OrderBy(r => r.Y).ThenBy(r => r.X).ToList();
        if (answers.Count == 0) return null;
        var qn = NormalizeQ(question.QuestionNumber);
        if (qn.Length > 0 && answers.FirstOrDefault(a => NormalizeQ(a.QuestionNumber) == qn) is { } byNumber) return byNumber;
        var nextQuestionY = pageRegions.Where(r => r.Label == RegionLabels.Question && r.Y > question.Y + 0.005f)
            .Select(r => r.Y).DefaultIfEmpty(1.01f).Min();
        return answers.FirstOrDefault(a => a.Y >= question.Y - 0.005f && a.Y < nextQuestionY
            && (a.QuestionNumber is null || qn.Length == 0 || NormalizeQ(a.QuestionNumber) == qn));
    }

    /// <summary>The question an answer belongs to (inverse of <see cref="AnswerFor"/>).</summary>
    public static Region? QuestionFor(Region answer, IReadOnlyCollection<Region> pageRegions) =>
        pageRegions.Where(r => r.Label == RegionLabels.Question).FirstOrDefault(q => AnswerFor(q, pageRegions)?.Id == answer.Id);

    /// <summary>question region id → answer region id, for the given question regions.</summary>
    public async Task<Dictionary<Guid, Guid>> AnswerLinksAsync(IReadOnlyCollection<Guid> questionIds, CancellationToken ct)
    {
        if (questionIds.Count == 0) return [];
        var pageIds = await db.Regions.AsNoTracking().Where(r => questionIds.Contains(r.Id)).Select(r => r.PageId).Distinct().ToListAsync(ct);
        var regions = await db.Regions.AsNoTracking().Where(r => pageIds.Contains(r.PageId)).ToListAsync(ct);
        var result = new Dictionary<Guid, Guid>();
        foreach (var page in regions.GroupBy(r => r.PageId))
        {
            var list = page.ToList();
            foreach (var q in list.Where(r => questionIds.Contains(r.Id)))
                if (AnswerFor(q, list) is { } a) result[q.Id] = a.Id;
        }
        return result;
    }

    public async Task<List<ResultCard>> HydrateAsync(IReadOnlyList<string> refs, Guid? userId, CancellationToken ct)
    {
        var regionIds = new List<Guid>();
        var pageIds = new List<Guid>();
        foreach (var r in refs)
        {
            if (!ResultRefs.TryParse(r, out var isRegion, out var id)) continue;
            (isRegion ? regionIds : pageIds).Add(id);
        }

        var regionPages = await db.Regions.AsNoTracking().Where(r => regionIds.Contains(r.Id)).Select(r => r.PageId).Distinct().ToListAsync(ct);
        var allPageIds = pageIds.Concat(regionPages).Distinct().ToList();
        var pages = await db.Pages.AsNoTracking().Include(p => p.File).ThenInclude(f => f!.Course).Include(p => p.Regions)
            .Where(p => allPageIds.Contains(p.Id))
            .AsSplitQuery()
            .ToDictionaryAsync(p => p.Id, ct);
        var regions = pages.Values.SelectMany(p => p.Regions).ToDictionary(r => r.Id);

        var saved = new HashSet<string>();
        if (userId is { } uid)
        {
            var marks = await db.Bookmarks.AsNoTracking().Where(b => b.UserId == uid && allPageIds.Contains(b.PageId))
                .Select(b => new { b.PageId, b.RegionId }).ToListAsync(ct);
            foreach (var m in marks) saved.Add(m.RegionId is { } rid ? ResultRefs.Region(rid) : ResultRefs.Page(m.PageId));
        }

        var cards = new List<ResultCard>();
        foreach (var r in refs.Distinct())
        {
            if (!ResultRefs.TryParse(r, out var isRegion, out var id)) continue;
            if (isRegion)
            {
                if (!regions.TryGetValue(id, out var reg) || !pages.TryGetValue(reg.PageId, out var page)) continue;
                if (reg.Label == RegionLabels.Question)
                {
                    cards.Add(Build(r, "question", page, reg.Id, reg.QuestionNumber, BoxOf(reg), saved.Contains(r), null));
                }
                else if (reg.Label == RegionLabels.Answer)
                {
                    // Older chats/bookmarks can still reference answer regions. Show the question or original page.
                    var question = QuestionFor(reg, page.Regions);
                    if (question is not null)
                        cards.Add(Build(ResultRefs.Region(question.Id), "question", page, question.Id, question.QuestionNumber,
                            BoxOf(question), saved.Contains(ResultRefs.Region(question.Id)), null));
                    else
                        cards.Add(Build(ResultRefs.Page(page.Id), "page", page, null, null, null,
                            saved.Contains(ResultRefs.Page(page.Id)), null));
                }
            }
            else if (pages.TryGetValue(id, out var page))
            {
                cards.Add(Build(r, page.Kind == PageKinds.Slide ? "slide" : "page", page, null, null, null, saved.Contains(r), null));
            }
        }
        return cards.DistinctBy(c => c.Ref).ToList();
    }

    private static Box BoxOf(Region r) => new(r.X, r.Y, r.W, r.H);

    public static ResultCard Build(string r, string kind, Page page, Guid? regionId, string? qn, Box? box, bool saved, Region? answer)
    {
        var f = page.File!;
        var c = f.Course!;
        var v = Version(page);
        var pageUrl = $"/api/media/pages/{page.Id}?v={v}";
        return new ResultCard(
            r, kind,
            regionId is { } rid ? $"/api/media/crops/{rid}?v={v}" : pageUrl,
            pageUrl,
            $"/api/media/thumbs/{page.Id}?v={v}",
            f.Id, page.Id, regionId, f.Title, f.OriginalName, f.SourceKind,
            c.Slug, c.Name, c.Color, c.Semester, f.MaterialType, f.AcademicYear ?? page.AcademicYear, f.AssessmentLabel,
            page.Number, f.PageCount, qn, page.Tags, box, page.Width, page.Height, saved,
            answer is null ? null : ResultRefs.Region(answer.Id),
            answer is null ? null : $"/api/media/crops/{answer.Id}?v={v}");
    }
}
