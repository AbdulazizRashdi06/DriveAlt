using DriveAlt.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DriveAlt.Api.Search;

public sealed record Box(float X, float Y, float W, float H);

/// <summary>What the UI renders for a result: images + source metadata. Never question/answer text.</summary>
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
    bool Saved);

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

    public async Task<List<ResultCard>> HydrateAsync(IReadOnlyList<string> refs, Guid? userId, CancellationToken ct)
    {
        var regionIds = new List<Guid>();
        var pageIds = new List<Guid>();
        foreach (var r in refs)
        {
            if (!ResultRefs.TryParse(r, out var isRegion, out var id)) continue;
            (isRegion ? regionIds : pageIds).Add(id);
        }

        var regions = await db.Regions.AsNoTracking().Where(r => regionIds.Contains(r.Id) && r.Label == RegionLabels.Question)
            .Select(r => new { r.Id, r.PageId, r.QuestionNumber, r.X, r.Y, r.W, r.H }).ToDictionaryAsync(r => r.Id, ct);
        var allPageIds = pageIds.Concat(regions.Values.Select(r => r.PageId)).Distinct().ToList();
        var pages = await db.Pages.AsNoTracking().Include(p => p.File).ThenInclude(f => f!.Course)
            .Where(p => allPageIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

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
                cards.Add(Build(r, "question", page, id, reg.QuestionNumber, new Box(reg.X, reg.Y, reg.W, reg.H), saved.Contains(r)));
            }
            else if (pages.TryGetValue(id, out var page))
            {
                cards.Add(Build(r, page.Kind == PageKinds.Slide ? "slide" : "page", page, null, null, null, saved.Contains(r)));
            }
        }
        return cards;
    }

    public static ResultCard Build(string r, string kind, Page page, Guid? regionId, string? qn, Box? box, bool saved)
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
            page.Number, f.PageCount, qn, page.Tags, box, page.Width, page.Height, saved);
    }
}
