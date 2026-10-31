using System.Security.Cryptography;
using DriveAlt.Api.Auth;
using DriveAlt.Api.Data;
using DriveAlt.Api.Infrastructure;
using DriveAlt.Api.Processing;
using DriveAlt.Api.Search;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DriveAlt.Api.Files;

public sealed record FileDto(
    Guid Id, string Title, string OriginalName, string SourceKind, long SizeBytes, int PageCount,
    string Status, int Step, int Progress, string? Error,
    int? CourseId, string? CourseSlug, string? CourseName, string? CourseColor, string MaterialType, int? Year, string? AssessmentLabel,
    string? Folder, string[] Guessed, DateTime CreatedAt, string? ThumbUrl, int Parts, FileWait? Wait = null);

/// <summary>
/// Why an unfinished file isn't moving, from its processing job: <c>reading</c> (a worker has it now), <c>queued</c>
/// (<c>Ahead</c> jobs before it), <c>paused</c> (AI usage limit or AI unavailable, until <c>Until</c>), <c>retrying</c>
/// (after an error, at <c>Until</c>), <c>held</c> (put on hold by an admin) or <c>stuck</c> (no job at all).
/// </summary>
public sealed record FileWait(string Reason, string? Detail = null, DateTime? Until = null, int? Ahead = null, int? Attempt = null);

public sealed record PageRegionDto(Guid Id, string? QuestionNumber, float X, float Y, float W, float H, bool Saved = false);

public sealed record PageThumbDto(Guid Id, int Number, string Kind, string ThumbUrl, string PageUrl, string[] Tags, int Questions, int Width, int Height,
    IReadOnlyList<PageRegionDto>? Regions = null, bool Saved = false);

public sealed record UploadCheckItem(string FileName, long SizeBytes, string? Sha256, int? CourseId = null);
public sealed record UploadCheckRequest(List<UploadCheckItem> Items);
public sealed record UploadCheckResult(int Index, string Kind, FileDto? Existing);

public sealed record FilePatch(int? CourseId, string? MaterialType, int? Year, string? AssessmentLabel, string? Folder, string? Title, bool ClearYear = false);

public static class FileEndpoints
{
    private static readonly FileExtensionContentTypeProvider Mime = new();

    public static FileDto ToDto(DocFile f)
    {
        var thumb = FileStatuses.Visible.Contains(f.Status) && f.PageCount > 0
            ? $"/api/media/files/{f.Id}/thumb?v={f.UpdatedAt.Ticks:x}"
            : null;
        return new FileDto(f.Id, f.Title, f.OriginalName, f.SourceKind, f.SizeBytes, f.PageCount, f.Status, FileStatuses.Step(f.Status),
            f.Progress, f.Error, f.CourseId, f.Course?.Slug, f.Course?.Name, f.Course?.Color, f.MaterialType, f.AcademicYear,
            f.AssessmentLabel, f.Folder, f.Guessed, f.CreatedAt, thumb, f.Parts.Length);
    }

    /// <summary>The reason each in-progress file is waiting, read from its latest processing job.</summary>
    internal static async Task<Dictionary<Guid, FileWait>> WaitsAsync(AppDb db, IReadOnlyCollection<Guid> fileIds, CancellationToken ct)
    {
        if (fileIds.Count == 0) return [];
        var now = DateTime.UtcNow;
        var jobs = (await db.Jobs.AsNoTracking().Where(j => fileIds.Contains(j.FileId) && j.Kind == JobKinds.Process
                && (j.State == "queued" || j.State == "running")).ToListAsync(ct))
            .GroupBy(j => j.FileId).ToDictionary(g => g.Key, g => g.OrderByDescending(j => j.Id).First());
        // Due jobs are claimed in id order, so a job's place in line is the due jobs before it.
        var due = await db.Jobs.AsNoTracking().Where(j => j.State == "queued" && j.RunAfter <= now).Select(j => j.Id).ToListAsync(ct);
        var result = new Dictionary<Guid, FileWait>();
        foreach (var id in fileIds)
        {
            if (!jobs.TryGetValue(id, out var j)) { result[id] = new FileWait("stuck"); continue; }
            result[id] = j switch
            {
                { State: "running" } => new FileWait("reading"),
                { Error: { } e } when e.StartsWith("Held") || j.RunAfter.Year >= 2100 => new FileWait("held", e),
                { Error: { } e } when e.StartsWith("Paused:") && j.RunAfter > now => new FileWait("paused", e["Paused:".Length..].Trim(), j.RunAfter),
                { Error: { } e } when j.RunAfter > now => new FileWait("retrying", e.Length > 300 ? e[..300] : e, j.RunAfter, Attempt: j.Attempts),
                _ when j.RunAfter > now => new FileWait("queued", Until: j.RunAfter),
                _ => new FileWait("queued", Ahead: due.Count(d => d < j.Id)),
            };
        }
        return result;
    }

    public static void Map(WebApplication app)
    {
        var up = app.MapGroup("/api").RequireAuthorization(AuthEndpoints.UploaderPolicy);

        up.MapPost("/uploads", async (HttpRequest req, HttpContext http, AppDb db, Storage storage, IOptions<StorageOptions> so,
            IOptions<GeminiOptions> go, CancellationToken ct) =>
        {
            var form = await req.ReadFormAsync(ct);
            var combine = form["combine"] == "true";
            int? defaultCourse = int.TryParse(form["courseId"], out var dc) ? dc : null;
            var defaultType = form["materialType"].ToString() is { Length: > 0 } dt ? MaterialTypes.Normalize(dt) : null;
            var files = form.Files.GetFiles("files");
            if (files.Count == 0) return Results.BadRequest(new { error = "No files." });
            var unsupported = files.Where(f => !DocumentConverter.IsSupported(f.FileName)).Select(f => f.FileName).ToList();
            if (unsupported.Count > 0)
                return Results.BadRequest(new { error = $"Can't read {string.Join(", ", unsupported)}. Use PDF, PPTX/PPT, DOCX or photos." });

            var courses = await db.Courses.AsNoTracking().ToListAsync(ct);
            var uid = http.User.RequiredUserId();
            var groups = combine && files.All(f => DocumentConverter.IsImage(f.FileName)) && files.Count > 1
                ? [files.ToList()]
                : files.Select(f => new List<IFormFile> { f }).ToList();

            var results = new List<object>();
            foreach (var group in groups)
            {
                var r = await SaveUploadAsync(group, form["title"], defaultCourse, defaultType, uid, courses, db, storage, ct);
                results.Add(r);
            }
            return Results.Ok(results);
        }).WithMetadata(new RequestSizeLimitAttribute(1024L * 1024 * 1024))
          .WithFormOptions(multipartBodyLengthLimit: 1024L * 1024 * 1024);

        up.MapPost("/uploads/check", async (UploadCheckRequest req, AppDb db, CancellationToken ct) =>
        {
            var items = req.Items ?? [];
            if (items.Count == 0) return Results.Ok(Array.Empty<UploadCheckResult>());

            var courses = await db.Courses.AsNoTracking().ToListAsync(ct);
            var hashes = items.Select(x => (x.Sha256 ?? "").Trim().ToLowerInvariant()).Where(h => h.Length > 0).Distinct().ToList();
            var byHash = hashes.Count == 0
                ? new Dictionary<string, DocFile>()
                : (await db.Files.AsNoTracking().Include(f => f.Course).Where(f => hashes.Contains(f.Sha256)).ToListAsync(ct))
                    .GroupBy(f => f.Sha256).ToDictionary(g => g.Key, g => g.First());

            // Titles only (not whole rows), canonicalised once per file rather than once per file per item.
            var titles = (await db.Files.AsNoTracking().OrderByDescending(f => f.CreatedAt)
                    .Select(f => new { f.Id, f.CourseId, f.Title, f.OriginalName }).ToListAsync(ct))
                .Select(f => (f.Id, f.CourseId, Title: Heuristics.CanonicalTitle(f.Title), Original: Heuristics.CanonicalTitle(f.OriginalName)))
                .ToList();

            var results = new List<UploadCheckResult>(items.Count);
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var h = (item.Sha256 ?? "").Trim().ToLowerInvariant();
                if (h.Length > 0 && byHash.TryGetValue(h, out var exact))
                {
                    results.Add(new UploadCheckResult(i, "exact", ToDto(exact)));
                    continue;
                }

                var norm = Heuristics.CanonicalTitle(item.FileName);
                if (norm.Length == 0)
                {
                    results.Add(new UploadCheckResult(i, "none", null));
                    continue;
                }

                var guessedCourseId = item.CourseId ?? Heuristics.FromPath(item.FileName, courses).Course?.Id;
                var match = titles.FirstOrDefault(f =>
                    (guessedCourseId is null || f.CourseId is null || f.CourseId == guessedCourseId) &&
                    (f.Title == norm || f.Original == norm));
                var similar = match.Id == Guid.Empty ? null
                    : await db.Files.AsNoTracking().Include(f => f.Course).FirstOrDefaultAsync(f => f.Id == match.Id, ct);

                results.Add(similar is not null
                    ? new UploadCheckResult(i, "similar", ToDto(similar))
                    : new UploadCheckResult(i, "none", null));
            }
            return Results.Ok(results);
        });

        up.MapGet("/uploads", async (HttpContext http, AppDb db, bool? all, CancellationToken ct) =>
        {
            var uid = http.User.RequiredUserId();
            var q = db.Files.AsNoTracking().Include(f => f.Course).AsQueryable();
            if (all != true) q = q.Where(f => f.UploadedById == uid);
            // Inspect (all=true) browses the whole drive as a tree, so it needs every file, not just the latest 200.
            var files = await q.OrderByDescending(f => f.CreatedAt).Take(all == true ? 10_000 : 200).ToListAsync(ct);
            var waits = await WaitsAsync(db, files.Where(f => FileStatuses.InProgress(f.Status)).Select(f => f.Id).ToList(), ct);
            return files.Select(f => ToDto(f) with { Wait = waits.GetValueOrDefault(f.Id) });
        });

        up.MapPatch("/files/{id:guid}", async (Guid id, FilePatch patch, AppDb db, CancellationToken ct) =>
        {
            var f = await db.Files.Include(x => x.Course).Include(x => x.Pages).ThenInclude(p => p.Regions).AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (f is null) return Results.NotFound();
            var guessed = f.Guessed.ToHashSet();
            if (patch.CourseId is { } cid)
            {
                if (!await db.Courses.AnyAsync(c => c.Id == cid, ct)) return Results.BadRequest(new { error = "Unknown course." });
                f.CourseId = cid;
                f.Course = await db.Courses.FindAsync([cid], ct);
                guessed.Remove("course");
            }
            if (patch.MaterialType is { } t) { f.MaterialType = MaterialTypes.Normalize(t); guessed.Remove("type"); }
            if (patch.Year is { } y) { f.AcademicYear = y is >= 2000 and <= 2100 ? y : null; guessed.Remove("year"); }
            if (patch.ClearYear) { f.AcademicYear = null; guessed.Remove("year"); }
            if (patch.AssessmentLabel is { } l) { f.AssessmentLabel = string.IsNullOrWhiteSpace(l) ? null : l.Trim(); guessed.Remove("label"); }
            if (patch.Folder is { } fo) f.Folder = string.IsNullOrWhiteSpace(fo) ? null : fo.Trim().Trim('/');
            if (patch.Title is { Length: > 0 } ti) f.Title = ti.Trim();
            f.Guessed = guessed.ToArray();
            f.UpdatedAt = DateTime.UtcNow;
            if (f.Status == FileStatuses.Ready)
            {
                foreach (var p in f.Pages) IndexText.Refresh(f, p);
                JobQueue.Enqueue(db, f.Id, JobKinds.Reembed);
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(f));
        });

        // Releases a file's held processing jobs (run_after far in the future, e.g. "Held: owner wants Semester 1 only"),
        // so it joins the normal queue now. Paused jobs (usage limit) are left alone: they resume on their own.
        up.MapPost("/files/{id:guid}/resume", async (Guid id, AppDb db, CancellationToken ct) =>
        {
            var now = DateTime.UtcNow;
            var released = await db.Jobs.Where(j => j.FileId == id && j.State == "queued"
                    && ((j.Error != null && j.Error.StartsWith("Held")) || j.RunAfter.Year >= 2100))
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.RunAfter, now).SetProperty(j => j.Error, (string?)null), ct);
            return released == 0 ? Results.NotFound(new { error = "Nothing on hold for this file." }) : Results.Ok(new { released });
        });

        up.MapPost("/files/{id:guid}/process", async (Guid id, AppDb db, CancellationToken ct) =>
        {
            var f = await db.Files.Include(x => x.Course).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (f is null) return Results.NotFound();
            if (f.CourseId is null) return Results.BadRequest(new { error = "Pick a course first." });
            // In progress with a live job: nothing to do. In progress with no job at all (stuck): queue it again.
            if (FileStatuses.InProgress(f.Status) && await db.Jobs.AnyAsync(j => j.FileId == id && j.Kind == JobKinds.Process
                    && (j.State == "queued" || j.State == "running"), ct))
                return Results.Ok(ToDto(f));
            f.Guessed = f.Guessed.Where(g => g is "year" or "label").ToArray();
            f.Status = FileStatuses.Queued;
            f.Error = null;
            f.UpdatedAt = DateTime.UtcNow;
            JobQueue.Enqueue(db, f.Id, JobKinds.Process);
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(f));
        });

        up.MapPost("/files/{id:guid}/reprocess", async (Guid id, AppDb db, Storage storage, CancellationToken ct) =>
        {
            var f = await db.Files.Include(x => x.Course).Include(x => x.Pages).ThenInclude(p => p.Regions).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (f is null) return Results.NotFound();
            if (f.CourseId is null) return Results.BadRequest(new { error = "Pick a course first." });
            foreach (var r in f.Pages.SelectMany(p => p.Regions)) Storage.TryDelete(storage.Crop(r.Id));
            db.Pages.RemoveRange(f.Pages);
            f.PageCount = 0;
            f.Progress = 0;
            f.Status = FileStatuses.Queued;
            f.Error = null;
            f.UpdatedAt = DateTime.UtcNow;
            JobQueue.Enqueue(db, f.Id, JobKinds.Process);
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(f));
        });

        up.MapDelete("/files/{id:guid}", async (Guid id, AppDb db, Storage storage, CancellationToken ct) =>
        {
            var f = await db.Files.Include(x => x.Pages).ThenInclude(p => p.Regions).AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (f is null) return Results.NotFound();
            var regionIds = f.Pages.SelectMany(p => p.Regions).Select(r => r.Id).ToList();
            await db.Jobs.Where(j => j.FileId == id).ExecuteDeleteAsync(ct);
            db.Files.Remove(f);
            await db.SaveChangesAsync(ct);
            storage.DeleteFile(id, regionIds);
            return Results.NoContent();
        });

        // ---------- read side (all signed-in users) ----------
        var g = app.MapGroup("/api").RequireAuthorization();

        g.MapGet("/files/{id:guid}", async (Guid id, HttpContext http, AppDb db, CancellationToken ct) =>
        {
            var uid = http.User.RequiredUserId();
            var f = await db.Files.AsNoTracking().Include(x => x.Course).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (f is null) return Results.NotFound();
            var pages = await db.Pages.AsNoTracking().Where(p => p.FileId == id).OrderBy(p => p.Number)
                .Select(p => new
                {
                    p.Id, p.Number, p.Kind, p.Tags, p.Width, p.Height, p.EditedAt,
                    Saved = db.Bookmarks.Any(b => b.UserId == uid && b.PageId == p.Id && b.RegionId == null),
                    Regions = p.Regions.Where(r => r.Label == RegionLabels.Question)
                        .OrderBy(r => r.Y).ThenBy(r => r.X)
                        .Select(r => new PageRegionDto(r.Id, r.QuestionNumber, r.X, r.Y, r.W, r.H,
                            db.Bookmarks.Any(b => b.UserId == uid && b.RegionId == r.Id)))
                        .ToList(),
                })
                .ToListAsync(ct);
            var ready = FileStatuses.Visible.Contains(f.Status) && pages.Count > 0;
            return Results.Ok(new
            {
                file = ToDto(f),
                pages = ready
                    ? pages.Select(p =>
                    {
                        var v = (p.EditedAt ?? f.UpdatedAt).Ticks.ToString("x");
                        return new PageThumbDto(p.Id, p.Number, p.Kind, $"/api/media/thumbs/{p.Id}?v={v}", $"/api/media/pages/{p.Id}?v={v}",
                            p.Tags, p.Regions.Count, p.Width, p.Height, p.Regions, p.Saved);
                    }).ToList()
                    : [],
            });
        });

        // Original file for viewing: PDF (original or converted from Office) or an image part.
        g.MapGet("/files/{id:guid}/view", async (Guid id, int? part, AppDb db, Storage storage, ProcessingService processing, CancellationToken ct) =>
        {
            var f = await db.Files.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (f is null) return Results.NotFound();
            if (f.SourceKind == SourceKinds.Images)
            {
                var idx = Math.Clamp((part ?? 1) - 1, 0, f.Parts.Length - 1);
                return ServeFile(storage.Original(f.Id, f.Parts[idx]), f.Parts[idx], inline: true);
            }
            var pdf = processing.PdfPath(f);
            return pdf is null ? Results.NotFound() : ServeFile(pdf, Path.ChangeExtension(f.OriginalName, ".pdf"), inline: true);
        });

        g.MapGet("/files/{id:guid}/download", async (Guid id, AppDb db, Storage storage, CancellationToken ct) =>
        {
            var f = await db.Files.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (f is null) return Results.NotFound();
            if (f.Parts.Length == 1) return ServeFile(storage.Original(f.Id, f.Parts[0]), f.OriginalName, inline: false);
            var zip = await ZipBuilder.BuildAsync(f.Parts.Select(p => (storage.Original(f.Id, p), p)), ct);
            return Results.File(zip, "application/zip", Storage.SafeName(f.Title) + ".zip");
        });

        // ---------- media ----------
        g.MapGet("/media/pages/{pageId:guid}", async (Guid pageId, HttpContext http, AppDb db, Storage storage, CancellationToken ct) =>
        {
            var p = await db.Pages.AsNoTracking().Where(x => x.Id == pageId).Select(x => new { x.FileId, x.Number }).FirstOrDefaultAsync(ct);
            return p is null ? Results.NotFound() : ServeImage(http, storage.PageImage(p.FileId, p.Number));
        });

        g.MapGet("/media/thumbs/{pageId:guid}", async (Guid pageId, HttpContext http, AppDb db, Storage storage, CancellationToken ct) =>
        {
            var p = await db.Pages.AsNoTracking().Where(x => x.Id == pageId).Select(x => new { x.FileId, x.Number }).FirstOrDefaultAsync(ct);
            return p is null ? Results.NotFound() : ServeImage(http, storage.Thumb(p.FileId, p.Number), storage.PageImage(p.FileId, p.Number));
        });

        g.MapGet("/media/files/{fileId:guid}/thumb", (Guid fileId, HttpContext http, Storage storage) => ServeImage(http, storage.Thumb(fileId, 1), storage.PageImage(fileId, 1)));

        g.MapGet("/media/crops/{regionId:guid}", async (Guid regionId, bool? download, HttpContext http, AppDb db, Storage storage, CancellationToken ct) =>
        {
            var r = await db.Regions.AsNoTracking().Where(x => x.Id == regionId && (x.Label == RegionLabels.Question || x.Label == RegionLabels.Answer))
                .Select(x => new { x.QuestionNumber, x.Label, x.Page!.Number, x.Page.FileId, x.Page.File!.Title }).FirstOrDefaultAsync(ct);
            if (r is null) return Results.NotFound();
            var path = storage.Crop(regionId);
            if (download == true)
                return File.Exists(path)
                    ? Results.File(path, "image/jpeg", Storage.SafeName($"{r.Title} p{r.Number}{(r.QuestionNumber is null ? "" : " Q" + r.QuestionNumber)}{(r.Label == RegionLabels.Answer ? " answer" : "")}.jpg"))
                    : Results.NotFound();
            // A box from a re-read that isn't cropped yet: show its whole page meanwhile.
            return ServeImage(http, path, storage.PageImage(r.FileId, r.Number));
        });

        // Same render, kept as its own route for the uploader comparison view.
        up.MapGet("/media/pages/{pageId:guid}/original", async (Guid pageId, HttpContext http, AppDb db, Storage storage, CancellationToken ct) =>
        {
            var p = await db.Pages.AsNoTracking().Where(x => x.Id == pageId).Select(x => new { x.FileId, x.Number }).FirstOrDefaultAsync(ct);
            return p is null ? Results.NotFound() : ServeImage(http, storage.PageImage(p.FileId, p.Number));
        });
    }

    public static string? MediaCacheControl(bool hasVersion, bool usedFallback) =>
        usedFallback ? "private, no-cache" : hasVersion ? "private, max-age=604800, immutable" : null;

    private static IResult ServeImage(HttpContext http, string path, string? fallback = null)
    {
        var usedFallback = false;
        if (!File.Exists(path) && fallback is not null)
        {
            path = fallback;
            usedFallback = true;
        }
        if (!File.Exists(path)) return Results.NotFound();
        if (MediaCacheControl(http.Request.Query.ContainsKey("v"), usedFallback) is { } cc)
            http.Response.Headers.CacheControl = cc;
        return Results.File(path, "image/jpeg", enableRangeProcessing: false,
            lastModified: File.GetLastWriteTimeUtc(path));
    }

    private static IResult ServeFile(string path, string name, bool inline)
    {
        if (!File.Exists(path)) return Results.NotFound();
        var type = Mime.TryGetContentType(name, out var t) ? t : "application/octet-stream";
        return inline
            ? Results.File(path, type, enableRangeProcessing: true)
            : Results.File(path, type, Storage.SafeName(name), enableRangeProcessing: true);
    }

    private static async Task<object> SaveUploadAsync(List<IFormFile> group, string? title, int? defaultCourse, string? defaultType, Guid uid,
        List<Course> courses, AppDb db, Storage storage, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var dir = storage.OriginalDir(id);
        Directory.CreateDirectory(dir);
        var parts = new List<string>();
        var hashes = new List<byte[]>();
        long size = 0;
        try
        {
            var i = 0;
            foreach (var f in group)
            {
                i++;
                var name = group.Count > 1 ? $"{i:D3}_{Storage.SafeName(f.FileName)}" : Storage.SafeName(f.FileName);
                await using (var src = f.OpenReadStream())
                await using (var dst = File.Create(Path.Combine(dir, name)))
                {
                    using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var buf = new byte[81920];
                    int n;
                    while ((n = await src.ReadAsync(buf, ct)) > 0)
                    {
                        sha.AppendData(buf, 0, n);
                        await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    }
                    hashes.Add(sha.GetHashAndReset());
                }
                parts.Add(name);
                size += f.Length;
            }
        }
        catch
        {
            Storage.TryDeleteDir(dir);
            throw;
        }

        var hash = Convert.ToHexString(hashes.Count == 1 ? hashes[0] : SHA256.HashData(hashes.SelectMany(h => h).ToArray())).ToLowerInvariant();
        var dup = await db.Files.AsNoTracking().Include(f => f.Course).FirstOrDefaultAsync(f => f.Sha256 == hash, ct);
        if (dup is not null)
        {
            Storage.TryDeleteDir(dir);
            return new { duplicate = true, file = ToDto(dup) };
        }

        var first = group[0].FileName;
        var kind = DocumentConverter.IsImage(first) ? SourceKinds.Images : DocumentConverter.IsOffice(first) ? SourceKinds.Office : SourceKinds.Pdf;
        var guess = Heuristics.FromPath(first, courses);
        var guessed = new List<string>();

        int? courseId = defaultCourse;
        if (courseId is null && guess.Course is not null) { courseId = guess.Course.Id; guessed.Add("course"); }
        if (courseId is null) guessed.Add("course");

        var type = defaultType;
        if (type is null)
        {
            type = guess.MaterialType ?? (kind == SourceKinds.Images && Heuristics.LooksLikePhoto(first) ? MaterialTypes.PastPaper : null);
            guessed.Add("type");
        }

        var file = new DocFile
        {
            Id = id,
            CourseId = courseId,
            MaterialType = type ?? MaterialTypes.Other,
            AcademicYear = guess.Year,
            AssessmentLabel = guess.Label,
            Title = string.IsNullOrWhiteSpace(title) ? Heuristics.TitleFromName(first) : title.Trim(),
            OriginalName = Path.GetFileName(first),
            SourceKind = kind,
            Parts = parts.ToArray(),
            Mime = group[0].ContentType,
            Sha256 = hash,
            SizeBytes = size,
            Guessed = guessed.Concat(guess.Year is null ? [] : ["year"]).Concat(guess.Label is null ? [] : ["label"]).ToArray(),
            UploadedById = uid,
        };
        db.Files.Add(file);
        if (file.Guessed.Contains("course") || file.Guessed.Contains("type"))
            JobQueue.Enqueue(db, file.Id, JobKinds.Suggest);
        await db.SaveChangesAsync(ct);
        file.Course = courses.FirstOrDefault(c => c.Id == file.CourseId);
        return new { duplicate = false, file = ToDto(file) };
    }
}
