using DriveAlt.Api.Auth;
using DriveAlt.Api.Data;
using DriveAlt.Api.Search;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DriveAlt.Api.Reports;

public static class ReportEndpoints
{
    public static readonly string[] Reasons = ["clipped_crop", "wrong_course", "missing_page", "irrelevant_result", "other"];
    public sealed record ReportBody(string? Ref, string? Reason, string? Details);

    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/reports").RequireAuthorization();
        g.MapPost("/", async (ReportBody body, HttpContext http, AppDb db, TimeProvider clock, CancellationToken ct) =>
        {
            if (body.Ref is null || !ResultRefs.TryParse(body.Ref, out var isRegion, out var targetId)
                || body.Reason is null || !Reasons.Contains(body.Reason) || (body.Details?.Length ?? 0) > 1000)
                return Results.BadRequest(new { error = "Choose a reason and keep the note under 1,000 characters." });
            var page = isRegion
                ? await db.Regions.Where(r => r.Id == targetId).Select(r => r.Page).FirstOrDefaultAsync(ct)
                : await db.Pages.FirstOrDefaultAsync(p => p.Id == targetId, ct);
            if (page is null || !await db.Files.AnyAsync(f => f.Id == page.FileId && FileStatuses.Visible.Contains(f.Status), ct))
                return Results.NotFound();
            var uid = http.User.RequiredUserId();
            Guid? regionId = isRegion ? targetId : null;
            var targetRef = isRegion ? ResultRefs.Region(targetId) : ResultRefs.Page(targetId);
            var existing = await db.MaterialReports.FirstOrDefaultAsync(r => r.UserId == uid
                && r.TargetRef == targetRef && r.Reason == body.Reason && r.Status == "open", ct);
            if (existing is not null) return Results.Ok(new { existing.Id, alreadyReported = true });
            var now = clock.GetUtcNow().UtcDateTime;
            if (await db.MaterialReports.CountAsync(r => r.UserId == uid && r.CreatedAt > now.AddHours(-1), ct) >= 20)
                return Results.Json(new { error = "You've sent lots of reports. Please try again in an hour." }, statusCode: 429);
            var report = new MaterialReport
            {
                UserId = uid, PageId = page.Id, RegionId = regionId, TargetRef = targetRef,
                Reason = body.Reason, Details = (body.Details ?? "").Trim(), CreatedAt = now,
            };
            db.MaterialReports.Add(report);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return Results.Conflict(new { error = "You've already reported this problem. Thanks!" });
            }
            return Results.Ok(new { report.Id, alreadyReported = false });
        });

        g.MapGet("/", async (string? status, int? page, AppDb db, CancellationToken ct) =>
        {
            status ??= "open";
            if (status is not ("open" or "resolved")) return Results.BadRequest();
            var number = Math.Clamp(page ?? 1, 1, 100000);
            var query = db.MaterialReports.AsNoTracking().Where(r => r.Status == status);
            var total = await query.CountAsync(ct);
            var items = await query.OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id).Skip((number - 1) * 30).Take(30)
                .Select(r => new
                {
                    r.Id, r.Reason, r.Details, r.Status, r.CreatedAt, r.ResolvedAt,
                    reporter = r.User!.UniId, fileId = r.Page!.FileId, fileTitle = r.Page.File!.Title,
                    courseName = r.Page.File.Course!.Name, pageNumber = r.Page.Number, r.RegionId,
                }).ToListAsync(ct);
            return Results.Ok(new { items, total, page = number, pageSize = 30 });
        }).RequireAuthorization(AuthEndpoints.UploaderPolicy);

        g.MapPost("/{id:guid}/resolve", async (Guid id, HttpContext http, AppDb db, TimeProvider clock, CancellationToken ct) =>
        {
            var report = await db.MaterialReports.FindAsync([id], ct);
            if (report is null) return Results.NotFound();
            if (report.Status != "resolved")
            {
                report.Status = "resolved";
                report.ResolvedAt = clock.GetUtcNow().UtcDateTime;
                report.ResolvedById = http.User.RequiredUserId();
                await db.SaveChangesAsync(ct);
            }
            return Results.Ok(new { report.Id, report.Status });
        }).RequireAuthorization(AuthEndpoints.UploaderPolicy);
    }
}
