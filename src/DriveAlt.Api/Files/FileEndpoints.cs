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
    string? Folder, string[] Guessed, DateTime CreatedAt, string? ThumbUrl, int Parts);

public sealed record PageThumbDto(Guid Id, int Number, string Kind, string ThumbUrl, string PageUrl, string[] Tags, int Questions, int Width, int Height);

public sealed record FilePatch(int? CourseId, string? MaterialType, int? Year, string? AssessmentLabel, string? Folder, string? Title, bool ClearYear = false);

public static class FileEndpoints
{
    private static readonly FileExtensionContentTypeProvider Mime = new();

    public static FileDto ToDto(DocFile f)
    {
        var thumb = f.Status == FileStatuses.Ready && f.PageCount > 0
            ? $"/api/media/files/{f.Id}/thumb?v={f.UpdatedAt.Ticks:x}"
            : null;
        return new FileDto(f.Id, f.Title, f.OriginalName, f.SourceKind, f.SizeBytes, f.PageCount, f.Status, FileStatuses.Step(f.Status),
            f.Progress, f.Error, f.CourseId, f.Course?.Slug, f.Course?.Name, f.Course?.Color, f.MaterialType, f.AcademicYear,
            f.AssessmentLabel, f.Folder, f.Guessed, f.CreatedAt, thumb, f.Parts.Length);
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

        up.MapGet("/uploads", async (HttpContext http, AppDb db, bool? all, CancellationToken ct) =>
        {
            var uid = http.User.RequiredUserId();
            var q = db.Files.AsNoTracking().Include(f => f.Course).AsQueryable();
            if (all != true) q = q.Where(f => f.UploadedById == uid);
            var files = await q.OrderByDescending(f => f.CreatedAt).Take(200).ToListAsync(ct);
            return files.Select(ToDto);
        });

        up.MapPatch("/files/{id:guid}", async (Guid id, FilePatch patch, AppDb db, CancellationToken ct) =>
        {
            var f = await db.Files.Include(x => x.Course).Include(x => x.Pages).ThenInclude(p => p.Regions).FirstOrDefaultAsync(x => x.Id == id, ct);
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

        up.MapPost("/files/{id:guid}/process", async (Guid id, AppDb db, CancellationToken ct) =>
        {
            var f = await db.Files.Include(x => x.Course).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (f is null) return Results.NotFound();
            if (f.CourseId is null) return Results.BadRequest(new { error = "Pick a course first." });
            if (FileStatuses.InProgress(f.Status)) return Results.Ok(ToDto(f));
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
            var f = await db.Files.Include(x => x.Pages).ThenInclude(p => p.Regions).FirstOrDefaultAsync(x => x.Id == id, ct);
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

        g.MapGet("/files/{id:guid}", async (Guid id, AppDb db, CancellationToken ct) =>
        {
            var f = await db.Files.AsNoTracking().Include(x => x.Course).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (f is null) return Results.NotFound();
            var pages = await db.Pages.AsNoTracking().Where(p => p.FileId == id).OrderBy(p => p.Number)
                .Select(p => new { p.Id, p.Number, p.Kind, p.Tags, p.Width, p.Height, p.EditedAt, Questions = p.Regions.Count(r => r.Label == RegionLabels.Question) })
                .ToListAsync(ct);
            var ready = f.Status == FileStatuses.Ready;
            return Results.Ok(new
            {
                file = ToDto(f),
                pages = ready
                    ? pages.Select(p =>
                    {
                        var v = (p.EditedAt ?? f.UpdatedAt).Ticks.ToString("x");
                        return new PageThumbDto(p.Id, p.Number, p.Kind, $"/api/media/thumbs/{p.Id}?v={v}", $"/api/media/pages/{p.Id}?v={v}",
                            p.Tags, p.Questions, p.Width, p.Height);
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
        g.MapGet("/media/pages/{pageId:guid}", async (Guid pageId, AppDb db, Storage storage, CancellationToken ct) =>
        {
            var p = await db.Pages.AsNoTracking().Where(x => x.Id == pageId).Select(x => new { x.FileId, x.Number }).FirstOrDefaultAsync(ct);
            return p is null ? Results.NotFound() : ServeImage(storage.PageImage(p.FileId, p.Number));
        });

        g.MapGet("/media/thumbs/{pageId:guid}", async (Guid pageId, AppDb db, Storage storage, CancellationToken ct) =>
        {
            var p = await db.Pages.AsNoTracking().Where(x => x.Id == pageId).Select(x => new { x.FileId, x.Number }).FirstOrDefaultAsync(ct);
            return p is null ? Results.NotFound() : ServeImage(storage.Thumb(p.FileId, p.Number));
        });

        g.MapGet("/media/files/{fileId:guid}/thumb", (Guid fileId, Storage storage) => ServeImage(storage.Thumb(fileId, 1)));

        g.MapGet("/media/crops/{regionId:guid}", async (Guid regionId, bool? download, AppDb db, Storage storage, CancellationToken ct) =>
        {
            var r = await db.Regions.AsNoTracking().Where(x => x.Id == regionId && (x.Label == RegionLabels.Question || x.Label == RegionLabels.Answer))
                .Select(x => new { x.QuestionNumber, x.Label, x.Page!.Number, x.Page.File!.Title }).FirstOrDefaultAsync(ct);
            if (r is null) return Results.NotFound();
            var path = storage.Crop(regionId);
            if (download == true)
                return File.Exists(path)
                    ? Results.File(path, "image/jpeg", Storage.SafeName($"{r.Title} p{r.Number}{(r.QuestionNumber is null ? "" : " Q" + r.QuestionNumber)}{(r.Label == RegionLabels.Answer ? " answer" : "")}.jpg"))
                    : Results.NotFound();
            return ServeImage(path);
        });

        // Same render, kept as its own route for the uploader comparison view.
        up.MapGet("/media/pages/{pageId:guid}/original", async (Guid pageId, AppDb db, Storage storage, CancellationToken ct) =>
        {
            var p = await db.Pages.AsNoTracking().Where(x => x.Id == pageId).Select(x => new { x.FileId, x.Number }).FirstOrDefaultAsync(ct);
            return p is null ? Results.NotFound() : ServeImage(storage.PageImage(p.FileId, p.Number));
        });
    }

    private static IResult ServeImage(string path)
    {
        if (!File.Exists(path)) return Results.NotFound();
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
