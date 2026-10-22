using DriveAlt.Api.Auth;
using DriveAlt.Api.Data;
using DriveAlt.Api.Files;
using DriveAlt.Api.Search;
using Microsoft.EntityFrameworkCore;

namespace DriveAlt.Api.Bookmarks;

public static class BookmarkEndpoints
{
    public sealed record SaveBody(string Ref);

    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/bookmarks").RequireAuthorization();

        // Saved crops grouped by course.
        g.MapGet("/", async (HttpContext http, AppDb db, ResultCards cards, CancellationToken ct) =>
        {
            var uid = http.User.RequiredUserId();
            var marks = await db.Bookmarks.AsNoTracking().Where(b => b.UserId == uid).OrderByDescending(b => b.CreatedAt)
                .Select(b => new { b.PageId, b.RegionId }).ToListAsync(ct);
            var refs = marks.Select(b => b.RegionId is { } r ? ResultRefs.Region(r) : ResultRefs.Page(b.PageId)).ToList();
            var list = await cards.HydrateAsync(refs, uid, ct);
            return list.GroupBy(c => new { c.CourseSlug, c.CourseName, c.CourseColor })
                .Select(gr => new { course = gr.Key.CourseSlug, name = gr.Key.CourseName, color = gr.Key.CourseColor, items = gr.ToList() });
        });

        g.MapPost("/", async (SaveBody body, HttpContext http, AppDb db, CancellationToken ct) =>
        {
            var uid = http.User.RequiredUserId();
            if (!ResultRefs.TryParse(body.Ref, out var isRegion, out var id)) return Results.BadRequest();
            Guid pageId;
            Guid? regionId = null;
            if (isRegion)
            {
                var r = await db.Regions.AsNoTracking().Where(x => x.Id == id && (x.Label == RegionLabels.Question || x.Label == RegionLabels.Answer)).Select(x => new { x.PageId }).FirstOrDefaultAsync(ct);
                if (r is null) return Results.NotFound();
                pageId = r.PageId;
                regionId = id;
            }
            else
            {
                if (!await db.Pages.AnyAsync(p => p.Id == id, ct)) return Results.NotFound();
                pageId = id;
            }
            var exists = await db.Bookmarks.AnyAsync(b => b.UserId == uid && b.PageId == pageId && b.RegionId == regionId, ct);
            if (!exists)
            {
                db.Bookmarks.Add(new Bookmark { UserId = uid, PageId = pageId, RegionId = regionId });
                await db.SaveChangesAsync(ct);
            }
            return Results.Ok(new { saved = true });
        });

        g.MapDelete("/{ref}", async (string @ref, HttpContext http, AppDb db, CancellationToken ct) =>
        {
            var uid = http.User.RequiredUserId();
            if (!ResultRefs.TryParse(@ref, out var isRegion, out var id)) return Results.BadRequest();
            var q = db.Bookmarks.Where(b => b.UserId == uid);
            q = isRegion ? q.Where(b => b.RegionId == id) : q.Where(b => b.PageId == id && b.RegionId == null);
            await q.ExecuteDeleteAsync(ct);
            return Results.Ok(new { saved = false });
        });

        // ZIP of saved images, optionally one course.
        g.MapGet("/download", async (string? course, HttpContext http, AppDb db, Storage storage, CancellationToken ct) =>
        {
            var uid = http.User.RequiredUserId();
            var q = db.Bookmarks.AsNoTracking().Where(b => b.UserId == uid);
            if (!string.IsNullOrWhiteSpace(course)) q = q.Where(b => b.Page!.File!.Course!.Slug == course);
            var items = await q.OrderBy(b => b.CreatedAt).Select(b => new
            {
                b.RegionId,
                b.Page!.FileId,
                b.Page.Number,
                b.Page.File!.Title,
                Course = b.Page.File.Course!.Name,
                QuestionNumber = b.Region != null ? b.Region.QuestionNumber : null,
            }).ToListAsync(ct);
            if (items.Count == 0) return Results.NotFound();
            var name = string.IsNullOrWhiteSpace(course) ? "saved" : $"saved-{course}";
            var zip = await ZipBuilder.BuildAsync(items.Select(i => (
                i.RegionId is { } rid ? storage.Crop(rid) : storage.PageImage(i.FileId, i.Number),
                Storage.SafeName($"{i.Course} - {i.Title} - p{i.Number}{(i.QuestionNumber is null ? "" : " Q" + i.QuestionNumber)}") + ".jpg")), ct);
            return Results.File(zip, "application/zip", name + ".zip");
        });
    }
}
