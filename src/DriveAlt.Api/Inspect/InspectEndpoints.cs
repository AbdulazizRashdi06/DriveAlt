using DriveAlt.Api.Auth;
using DriveAlt.Api.Data;
using DriveAlt.Api.Files;
using DriveAlt.Api.Processing;
using DriveAlt.Api.Search;
using Microsoft.EntityFrameworkCore;

namespace DriveAlt.Api.Inspect;

/// <summary>
/// The uploader comparison view: original page next to what the AI understood. No approval step: edits just fix
/// the extraction (text, tags, regions) and re-render crops/masks.
/// </summary>
public static class InspectEndpoints
{
    public sealed record RegionDto(Guid Id, string Label, float X, float Y, float W, float H, string? QuestionNumber, string Text, int Order, string? CropUrl);

    public sealed record InspectPageDto(
        Guid Id, int Number, int Width, int Height, string Kind, string Text, string AiSummary, string? Topic, string[] Tags,
        int? Year, string? Error, DateTime? EditedAt, string OriginalUrl, string MaskedUrl, List<RegionDto> Regions);

    public sealed record RegionEdit(Guid? Id, string Label, float X, float Y, float W, float H, string? QuestionNumber, string? Text);

    public sealed record PageEdit(string? Kind, string? Text, string? Topic, string[]? Tags, List<RegionEdit>? Regions);

    public static InspectPageDto ToDto(Page p, DocFile f)
    {
        var v = ResultCards.Version(p);
        return new InspectPageDto(p.Id, p.Number, p.Width, p.Height, p.Kind, p.Text, p.AiSummary, p.Topic, p.Tags, p.AcademicYear, p.Error,
            p.EditedAt, $"/api/media/pages/{p.Id}/original?v={v}", $"/api/media/pages/{p.Id}?v={v}",
            p.Regions.OrderBy(r => r.Order).Select(r => new RegionDto(r.Id, r.Label, r.X, r.Y, r.W, r.H, r.QuestionNumber, r.Text, r.Order,
                r.Label == RegionLabels.Question ? $"/api/media/crops/{r.Id}?v={v}" : null)).ToList());
    }

    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/inspect").RequireAuthorization(AuthEndpoints.UploaderPolicy);

        g.MapGet("/files/{id:guid}", async (Guid id, AppDb db, CancellationToken ct) =>
        {
            var f = await db.Files.AsNoTracking().Include(x => x.Course).Include(x => x.Pages).ThenInclude(p => p.Regions)
                .AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (f is null) return Results.NotFound();
            return Results.Ok(new
            {
                file = FileEndpoints.ToDto(f),
                pages = f.Pages.Where(p => p.Extracted).OrderBy(p => p.Number).Select(p => ToDto(p, f)),
            });
        });

        g.MapPut("/pages/{id:guid}", async (Guid id, PageEdit edit, HttpContext http, AppDb db, Storage storage, PageRenderer renderer, CancellationToken ct) =>
        {
            var page = await db.Pages.Include(p => p.Regions).Include(p => p.File).ThenInclude(f => f!.Course)
                .FirstOrDefaultAsync(p => p.Id == id, ct);
            if (page is null) return Results.NotFound();
            var file = page.File!;

            if (edit.Kind is { } k) page.Kind = PageKinds.Normalize(k);
            if (edit.Text is { } t) page.Text = t;
            if (edit.Topic is { } to) page.Topic = string.IsNullOrWhiteSpace(to) ? null : to.Trim();
            if (edit.Tags is { } tags)
                page.Tags = tags.Select(x => x.Trim().ToLowerInvariant()).Where(x => x.Length > 0).Distinct().Take(16).ToArray();

            var removedCrops = new List<Guid>();
            if (edit.Regions is { } regions)
            {
                var existing = page.Regions.ToDictionary(r => r.Id);
                var keep = new HashSet<Guid>();
                var order = 0;
                foreach (var e in regions)
                {
                    var (x, y, w, h) = Clamp(e.X, e.Y, e.W, e.H);
                    if (w < 0.005f || h < 0.005f) continue;
                    Region r;
                    if (e.Id is { } rid && existing.TryGetValue(rid, out var found))
                    {
                        r = found;
                    }
                    else
                    {
                        r = new Region { PageId = page.Id };
                        db.Regions.Add(r);
                    }
                    var label = RegionLabels.Normalize(e.Label);
                    if (r.Label == RegionLabels.Question && label != RegionLabels.Question) removedCrops.Add(r.Id);
                    r.Label = label;
                    (r.X, r.Y, r.W, r.H) = (x, y, w, h);
                    r.QuestionNumber = string.IsNullOrWhiteSpace(e.QuestionNumber) ? null : e.QuestionNumber.Trim();
                    if (e.Text is not null) r.Text = e.Text;
                    r.Order = order++;
                    if (label != RegionLabels.Question) r.Embedding = null;
                    keep.Add(r.Id);
                    if (!page.Regions.Contains(r)) page.Regions.Add(r);
                }
                foreach (var gone in page.Regions.Where(r => !keep.Contains(r.Id)).ToList())
                {
                    removedCrops.Add(gone.Id);
                    page.Regions.Remove(gone);
                    db.Regions.Remove(gone);
                }
            }

            page.EditedAt = DateTime.UtcNow;
            page.EditedById = http.User.RequiredUserId();
            IndexText.Refresh(file, page);
            JobQueue.Enqueue(db, file.Id, JobKinds.Reembed, page.Id);
            await db.SaveChangesAsync(ct);

            foreach (var rid in removedCrops) Storage.TryDelete(storage.Crop(rid));
            await renderer.RenderAsync(file.Id, page, ct);
            return Results.Ok(ToDto(page, file));
        });
    }

    private static (float, float, float, float) Clamp(float x, float y, float w, float h)
    {
        x = Math.Clamp(x, 0, 1);
        y = Math.Clamp(y, 0, 1);
        w = Math.Clamp(w, 0, 1 - x);
        h = Math.Clamp(h, 0, 1 - y);
        return (x, y, w, h);
    }
}
