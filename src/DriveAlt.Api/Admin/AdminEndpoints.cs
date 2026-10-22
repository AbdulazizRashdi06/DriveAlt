using DriveAlt.Api.Auth;
using DriveAlt.Api.Data;
using DriveAlt.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DriveAlt.Api.Admin;

public static class AdminEndpoints
{
    public sealed record RoleBody(string Role);

    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/admin").RequireAuthorization(AuthEndpoints.UploaderPolicy);

        g.MapGet("/users", async (AppDb db, IOptions<AuthOptions> opts, CancellationToken ct) =>
        {
            var pinned = opts.Value.Uploaders.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var users = await db.Users.AsNoTracking().OrderByDescending(u => u.Role).ThenBy(u => u.UniId).ToListAsync(ct);
            return users.Select(u => new { u.Id, u.UniId, u.Email, u.Role, u.LastLoginAt, pinned = pinned.Contains(u.UniId) });
        });

        g.MapPut("/users/{id:guid}/role", async (Guid id, RoleBody body, AppDb db, IOptions<AuthOptions> opts, CancellationToken ct) =>
        {
            if (body.Role is not (Roles.Student or Roles.Uploader)) return Results.BadRequest();
            var user = await db.Users.FindAsync([id], ct);
            if (user is null) return Results.NotFound();
            if (body.Role == Roles.Student && opts.Value.Uploaders.Contains(user.UniId, StringComparer.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = "This uploader is set in the server config." });
            user.Role = body.Role;
            await db.SaveChangesAsync(ct);
            RoleCache.Forget(user.Id);
            return Results.Ok(new { user.Id, user.Role });
        });

        g.MapGet("/stats", async (AppDb db, CancellationToken ct) => new
        {
            files = await db.Files.CountAsync(ct),
            ready = await db.Files.CountAsync(f => f.Status == FileStatuses.Ready, ct),
            processing = await db.Files.CountAsync(f => f.Status != FileStatuses.Ready && f.Status != FileStatuses.Failed && f.Status != FileStatuses.Uploaded, ct),
            failed = await db.Files.CountAsync(f => f.Status == FileStatuses.Failed, ct),
            pages = await db.Pages.CountAsync(ct),
            questions = await db.Regions.CountAsync(r => r.Label == RegionLabels.Question, ct),
            users = await db.Users.CountAsync(ct),
        });
    }
}
