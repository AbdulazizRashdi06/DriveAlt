using DriveAlt.Api.Auth;
using DriveAlt.Api.Data;
using DriveAlt.Api.Files;
using DriveAlt.Api.Search;
using Microsoft.EntityFrameworkCore;

namespace DriveAlt.Api.Browse;

public static class BrowseEndpoints
{
    public sealed record CourseDto(int Id, string Slug, string Name, int Semester, string Group, string Color, string[] Aliases, int Files);

    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api").RequireAuthorization();

        // The Year 2 tree: semester → group → course, like the batch Drive.
        g.MapGet("/courses", async (AppDb db, CancellationToken ct) =>
        {
            var counts = await db.Files.AsNoTracking().Where(f => f.CourseId != null && (f.Status == FileStatuses.Ready || (FileStatuses.Visible.Contains(f.Status) && f.Pages.Any(p => p.Text != "" || p.Regions.Any()))))
                .GroupBy(f => f.CourseId!.Value).Select(x => new { x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            var courses = await db.Courses.AsNoTracking().OrderBy(c => c.Sort).ToListAsync(ct);
            return courses.Select(c => new CourseDto(c.Id, c.Slug, c.Name, c.Semester, c.Group, c.Color, c.Aliases, counts.GetValueOrDefault(c.Id)));
        });

        // One course: folders (type + subfolder) with their files, plus tag facets.
        g.MapGet("/courses/{slug}", async (string slug, string? type, string? folder, string? tag, int? year, AppDb db, CancellationToken ct) =>
        {
            var course = await db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Slug == slug, ct);
            if (course is null) return Results.NotFound();

            var all = await db.Files.AsNoTracking().Include(f => f.Course)
                .Where(f => f.CourseId == course.Id && (f.Status == FileStatuses.Ready || (FileStatuses.Visible.Contains(f.Status) && f.Pages.Any(p => p.Text != "" || p.Regions.Any()))))
                .OrderByDescending(f => f.AcademicYear).ThenBy(f => f.Folder).ThenBy(f => f.Title)
                .ToListAsync(ct);

            var typeCounts = all.GroupBy(f => f.MaterialType).ToDictionary(x => x.Key, x => x.Count());
            var inType = type is null ? all : all.Where(f => f.MaterialType == type).ToList();

            // Subfolders directly below the current folder ("midterm/midterm 1" → "midterm" at the top).
            var prefix = string.IsNullOrWhiteSpace(folder) ? "" : folder.Trim('/') + "/";
            var folders = inType
                .Where(f => f.Folder is not null && (prefix == "" || (f.Folder + "/").StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                .Select(f => (f.Folder + "/")[prefix.Length..].TrimEnd('/'))
                .Where(rest => rest.Length > 0)
                .Select(rest => rest.Split('/')[0])
                .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Select(x => new { name = x.Key, path = prefix + x.Key, files = x.Count() })
                .OrderBy(x => x.name).ToList();

            // Like Drive: only the files directly in this folder; deeper ones live in the subfolders above.
            var current = prefix.TrimEnd('/');
            var here = inType.Where(f => string.Equals(f.Folder ?? "", current, StringComparison.OrdinalIgnoreCase)).ToList();

            var fileIds = here.Select(f => f.Id).ToList();
            var pageTags = await db.Pages.AsNoTracking().Where(p => fileIds.Contains(p.FileId)).Select(p => new { p.FileId, p.Tags }).ToListAsync(ct);
            var tagCounts = pageTags.SelectMany(p => p.Tags).GroupBy(t => t).OrderByDescending(t => t.Count()).ThenBy(t => t.Key)
                .Take(24).Select(t => new { tag = t.Key, pages = t.Count() }).ToList();
            if (!string.IsNullOrWhiteSpace(tag))
            {
                var withTag = pageTags.Where(p => p.Tags.Contains(tag)).Select(p => p.FileId).ToHashSet();
                here = here.Where(f => withTag.Contains(f.Id)).ToList();
            }
            if (year is { } y) here = here.Where(f => f.AcademicYear == y).ToList();

            return Results.Ok(new
            {
                course = new CourseDto(course.Id, course.Slug, course.Name, course.Semester, course.Group, course.Color, course.Aliases, all.Count),
                types = typeCounts,
                folders,
                files = here.Select(FileEndpoints.ToDto),
                tags = tagCounts,
                years = inType.Where(f => f.AcademicYear != null).Select(f => f.AcademicYear!.Value).Distinct().OrderDescending(),
            });
        });

        // Keyword search without the bot (Browse search box + tag chips).
        g.MapGet("/search", async (string? q, string? course, string? type, int? year, string? tag, Guid? file, HttpContext http,
            AppDb db, SearchService search, ResultCards cards, CancellationToken ct) =>
        {
            int[]? courseIds = null;
            if (!string.IsNullOrWhiteSpace(course))
            {
                var c = await db.Courses.AsNoTracking().FirstOrDefaultAsync(x => x.Slug == course, ct);
                if (c is null) return Results.NotFound();
                courseIds = [c.Id];
            }
            var hits = await search.SearchAsync(new SearchQuery(q ?? "", courseIds, type is null ? null : [type], year, null, tag, file, Limit: 48), ct);
            return Results.Ok(await cards.HydrateAsync(hits.Select(h => h.Ref).ToList(), http.User.UserId(), ct));
        });

        // Hydrate specific card refs (for deep-link sharing via ?ref=r:... or ?ref=p:...).
        g.MapGet("/cards", async (string? refs, HttpContext http, ResultCards cards, CancellationToken ct) =>
        {
            var list = (refs ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct().Take(24).ToList();
            if (list.Count == 0) return Results.Ok(Array.Empty<ResultCard>());
            return Results.Ok(await cards.HydrateAsync(list, http.User.UserId(), ct));
        });
    }
}
