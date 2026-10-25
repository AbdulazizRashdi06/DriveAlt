using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DriveAlt.Api.Data;
using DriveAlt.Api.Files;
using DriveAlt.Api.Infrastructure;
using DriveAlt.Api.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DriveAlt.Api.Import;

/// <summary>
/// One-off import of the batch's public Year 2 Google Drive folder. Maps the Drive folder path to course / type /
/// year / subfolder, groups loose photos in a folder into one multi-page file, skips videos and archives, and queues
/// everything through the normal pipeline. Idempotent: files already imported (by Drive id or content hash) are skipped.
///
/// Usage: dotnet DriveAlt.Api.dll import-drive [folderId] [--only "semester 1/ALL/data structures"] [--skip "semester 1/AI/ITS"]... [--limit N] [--dry-run] [--uploader 24-0161]
///        [--from-local /data/import-local]   (plan from a local copy: manifest.json + originals/, only Drive files the app doesn't have)
/// </summary>
public sealed partial class DriveImporter(HttpClient http, AppDb db, Storage storage, IOptions<GoogleOptions> google,
    IOptions<AuthOptions> auth, ILogger<DriveImporter> log)
{
    public const string Year2FolderId = "1UrcEiKblimjLCU1R5SLCDIA3N4K7zBwd";

    private static readonly string[] GroupFolders = ["ai", "all", "cs and cys", "cs & cys", "cys", "cs"];
    private static readonly string[] SkipExtensions = [".mp4", ".mov", ".m4a", ".mp3", ".zip", ".rar", ".7z", ".goodnotes", ".pb", ".exe"];

    /// <param name="LocalPath">Set when the file comes from a local copy of the Drive (--from-local) instead of a download.</param>
    public sealed record DriveItem(string Id, string Name, string Kind, string? GoogleType, string? LocalPath = null);

    private sealed record Planned(List<(DriveItem Item, string Path)> Items, string[] Segments, string Title);

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        var valued = new[] { "--only", "--limit", "--uploader", "--from-local", "--skip" };
        var folderId = args.Where((a, i) => !a.StartsWith("--") && (i == 0 || !valued.Contains(args[i - 1]))).FirstOrDefault() ?? Year2FolderId;
        var only = Arg(args, "--only")?.ToLowerInvariant().Trim('/');
        var limit = int.TryParse(Arg(args, "--limit"), out var l) ? l : int.MaxValue;
        var dryRun = args.Contains("--dry-run");
        var uploaderId = Arg(args, "--uploader") ?? auth.Value.Uploaders.FirstOrDefault() ?? "00-0000";
        var fromLocal = Arg(args, "--from-local");
        var skip = args.Select((a, i) => (a, i)).Where(x => x.a == "--skip" && x.i + 1 < args.Length)
            .Select(x => args[x.i + 1].ToLowerInvariant().Trim('/')).ToList();

        var uploader = await db.Users.FirstOrDefaultAsync(u => u.UniId == uploaderId, ct);
        if (uploader is null)
        {
            uploader = new User { UniId = uploaderId, Email = auth.Value.EmailTemplate.Replace("{id}", uploaderId), Role = Roles.Uploader };
            db.Users.Add(uploader);
            await db.SaveChangesAsync(ct);
        }
        var courses = await db.Courses.AsNoTracking().ToListAsync(ct);

        var plan = new List<Planned>();
        if (fromLocal is not null)
        {
            log.LogInformation("Planning from the local copy in {Dir} (only Drive files the app doesn't have yet)…", fromLocal);
            var have = (await db.Files.Where(f => f.SourceRef != null && f.SourceRef.StartsWith("gdrive:")).Select(f => f.SourceRef!).ToListAsync(ct))
                .SelectMany(r => r[7..].Split('+')).ToHashSet();
            plan = LocalPlan(fromLocal, have);
        }
        else
        {
            log.LogInformation("Listing Drive folder {Folder}…", folderId);
            await WalkAsync(folderId, [], plan, only, ct);
        }
        if (only is not null) plan = plan.Where(p => string.Join('/', p.Segments).ToLowerInvariant().StartsWith(only)).ToList();
        if (skip.Count > 0) plan = plan.Where(p => !skip.Any(string.Join('/', p.Segments).ToLowerInvariant().StartsWith)).ToList();

        int queued = 0, skipped = 0, failed = 0;
        foreach (var p in plan.Take(limit))
        {
            var refKey = "gdrive:" + string.Join("+", p.Items.Select(i => i.Item.Id));
            if (await db.Files.AnyAsync(f => f.SourceRef == refKey, ct)) { skipped++; continue; }

            var mapped = Map(p.Segments, p.Items[0].Path, courses);
            if (mapped.Course is null)
            {
                log.LogWarning("No course for {Path}, skipping", string.Join('/', p.Segments));
                skipped++;
                continue;
            }
            log.LogInformation("{Action} {Title} → {Course} / {Type} / {Folder} ({Count} part(s))", dryRun ? "Would import" : "Importing",
                p.Title, mapped.Course.Name, mapped.Type, mapped.Folder ?? "-", p.Items.Count);
            if (dryRun) { queued++; continue; }

            try
            {
                await ImportAsync(p, refKey, mapped, uploader.Id, ct);
                queued++;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                failed++;
                log.LogError(e, "Import failed for {Title}", p.Title);
            }
        }
        log.LogInformation("Done: {Queued} queued, {Skipped} skipped, {Failed} failed. The server's workers will process them.", queued, skipped, failed);
        return failed > 0 ? 1 : 0;
    }

    private static string? Arg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    public sealed record Mapping(Course? Course, int Semester, string Type, string? Folder, int? Year, string? Label);

    /// <summary>"semester 1/ALL/data structures/past papers/midterm/midterm 1" → Data Structures, past_paper, "midterm/midterm 1".</summary>
    public static Mapping Map(string[] segments, string fileName, IReadOnlyList<Course> courses)
    {
        var semester = segments.Length > 0 && segments[0].Contains('2') ? 2 : 1;
        var rest = segments.Skip(1).ToList();
        if (rest.Count > 0 && GroupFolders.Contains(rest[0].Trim().ToLowerInvariant())) rest.RemoveAt(0);

        Course? course = null;
        var ci = -1;
        for (var i = 0; i < rest.Count && course is null; i++)
        {
            course = Heuristics.MatchCourse(rest[i].ToLowerInvariant(), courses.Where(c => c.Semester == semester).ToList())
                     ?? Heuristics.MatchCourse(rest[i].ToLowerInvariant(), courses);
            if (course is not null) ci = i;
        }
        var sub = ci >= 0 ? rest.Skip(ci + 1).ToList() : rest;

        // The deepest folder that names a type wins ("past papers/quiz 1" → quiz), then the file name.
        string? type = null;
        for (var i = sub.Count - 1; i >= 0 && type is null; i--) type = Heuristics.MaterialType(sub[i].ToLowerInvariant());
        var fullLower = (string.Join(' ', segments) + " " + fileName).ToLowerInvariant();
        type ??= Heuristics.MaterialType(fileName.ToLowerInvariant())
                 ?? (fullLower.Contains("question") || fullLower.Contains(" act") ? MaterialTypes.Exercise : MaterialTypes.Other);

        // A top folder that only names the type ("slides", "past papers") is already the type segment in Browse.
        if (sub.Count > 0 && Heuristics.MaterialType(sub[0].ToLowerInvariant()) is not null && IsPlainTypeFolder(sub[0])) sub.RemoveAt(0);

        var folder = sub.Count == 0 ? null : string.Join('/', sub.Select(s => s.Trim()));
        var pathText = string.Join(' ', segments) + " " + fileName;
        return new Mapping(course, semester, type, folder, Heuristics.Year(pathText), Heuristics.Label(string.Join(' ', sub) + " " + fileName));
    }

    private static bool IsPlainTypeFolder(string name) => name.Trim().ToLowerInvariant() is
        "slides" or "slide" or "lectures" or "past papers" or "past paper" or "quizzes" or "quiz" or "exercises" or "labs" or "notes"
        or "mid & final" or "midterm & final" or "mid&final" or "quizzes & past papers" or "sql quizzes" or "tutorials" or "tutorial slides";

    private async Task WalkAsync(string folderId, string[] segments, List<Planned> plan, string? only, CancellationToken ct)
    {
        List<DriveItem> items;
        try
        {
            items = await ListAsync(folderId, ct);
        }
        catch (HttpRequestException e)
        {
            log.LogWarning("Could not list {Path} ({Error}), skipping it", string.Join('/', segments), e.Message);
            return;
        }
        var photos = new List<(DriveItem, string)>();
        foreach (var item in items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (item.Kind == "folder")
            {
                string[] sub = [.. segments, item.Name.Trim()];
                var path = string.Join('/', sub).ToLowerInvariant();
                if (only is null || path.StartsWith(only) || only.StartsWith(path + "/") || only == path)
                    await WalkAsync(item.Id, sub, plan, only, ct);
                continue;
            }
            var name = item.Name;
            if (item.GoogleType is not null) name += ".pdf"; // native Google Docs/Slides export as PDF
            var ext = Path.GetExtension(name).ToLowerInvariant();
            if (SkipExtensions.Contains(ext) || !DocumentConverter.IsSupported(name))
            {
                log.LogInformation("Skipping {Path}/{Name}", string.Join('/', segments), item.Name);
                continue;
            }
            if (DocumentConverter.IsImage(name)) photos.Add((item, name));
            else plan.Add(new Planned([(item, name)], segments, Heuristics.TitleFromName(name)));
        }
        if (photos.Count == 1)
            plan.Add(new Planned(photos, segments, Heuristics.TitleFromName(photos[0].Item2)));
        else if (photos.Count > 1)
            plan.Add(new Planned(photos, segments, segments.Length > 0 ? $"{segments[^1]} (photos)" : "Photos"));
    }

    // ---------- local copy (--from-local) ----------

    /// <summary>
    /// Plans files from a local copy of the Drive (DriveAlt-local: manifest.json + originals/), for Drive files the app is missing,
    /// e.g. folders whose listing failed or names without an extension. Grouping follows WalkAsync: loose photos in one folder
    /// become one file.
    /// </summary>
    private List<Planned> LocalPlan(string dir, HashSet<string> have)
    {
        var plan = new List<Planned>();
        var files = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "manifest.json")))!["files"]!.AsArray()
            .Select(f => (Id: f!["id"]!.GetValue<string>(), Path: f["path"]!.GetValue<string>(), Status: f["status"]?.GetValue<string>()))
            .Where(f => f.Status == "complete" && !have.Contains(f.Id));
        foreach (var folder in files.GroupBy(f => f.Path.Contains('/') ? f.Path[..f.Path.LastIndexOf('/')] : ""))
        {
            var segments = folder.Key.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var photos = new List<(DriveItem, string)>();
            foreach (var f in folder.OrderBy(f => f.Path, StringComparer.Ordinal))
            {
                var local = Path.Combine([dir, "originals", .. f.Path.Split('/')]);
                if (!File.Exists(local)) continue;
                var name = f.Path[(f.Path.LastIndexOf('/') + 1)..];
                if (!DocumentConverter.IsSupported(name) && SniffExtension(local) is { } ext) name += ext;
                if (SkipExtensions.Contains(Path.GetExtension(name).ToLowerInvariant()) || !DocumentConverter.IsSupported(name))
                {
                    log.LogInformation("Skipping {Path}", f.Path);
                    continue;
                }
                var item = new DriveItem(f.Id, name, "file", null, local);
                if (DocumentConverter.IsImage(name)) photos.Add((item, name));
                else plan.Add(new Planned([(item, name)], segments, Heuristics.TitleFromName(name)));
            }
            if (photos.Count == 1) plan.Add(new Planned(photos, segments, Heuristics.TitleFromName(photos[0].Item2)));
            else if (photos.Count > 1) plan.Add(new Planned(photos, segments, segments.Length > 0 ? $"{segments[^1]} (photos)" : "Photos"));
        }
        return plan;
    }

    /// <summary>The extension a file's content implies, for Drive names without one (e.g. "q51" is a JPEG).</summary>
    internal static string? SniffExtension(string path)
    {
        var head = new byte[12];
        using (var fs = File.OpenRead(path)) if (fs.Read(head, 0, head.Length) < 4) return null;
        if (head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return ".jpg";
        if (head[0] == 0x89 && head[1] == (byte)'P' && head[2] == (byte)'N' && head[3] == (byte)'G') return ".png";
        if (head[0] == (byte)'%' && head[1] == (byte)'P' && head[2] == (byte)'D' && head[3] == (byte)'F') return ".pdf";
        var brand = System.Text.Encoding.ASCII.GetString(head, 4, 8);
        if (brand is "ftypheic" or "ftypheix" or "ftypmif1") return ".heic";
        if (head[0] == (byte)'P' && head[1] == (byte)'K')
        {
            try
            {
                using var zip = System.IO.Compression.ZipFile.OpenRead(path);
                if (zip.Entries.Any(e => e.FullName.StartsWith("word/"))) return ".docx";
                if (zip.Entries.Any(e => e.FullName.StartsWith("ppt/"))) return ".pptx";
            }
            catch (InvalidDataException) { }
        }
        return null;
    }

    // ---------- listing ----------

    private async Task<List<DriveItem>> ListAsync(string folderId, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(google.Value.ApiKey) ? await ScrapeListAsync(folderId, ct) : await ApiListAsync(folderId, ct);

    private async Task<List<DriveItem>> ApiListAsync(string folderId, CancellationToken ct)
    {
        var result = new List<DriveItem>();
        string? pageToken = null;
        do
        {
            var url = "https://www.googleapis.com/drive/v3/files?pageSize=1000&fields=nextPageToken,files(id,name,mimeType)" +
                      $"&q={WebUtility.UrlEncode($"'{folderId}' in parents and trashed=false")}&key={google.Value.ApiKey}" +
                      (pageToken is null ? "" : $"&pageToken={pageToken}");
            var json = JsonNode.Parse(await http.GetStringAsync(url, ct))!;
            foreach (var f in json["files"]!.AsArray())
            {
                var mime = f!["mimeType"]!.GetValue<string>();
                var kind = mime == "application/vnd.google-apps.folder" ? "folder" : "file";
                var g = mime switch
                {
                    "application/vnd.google-apps.document" => "document",
                    "application/vnd.google-apps.presentation" => "presentation",
                    _ => null,
                };
                if (mime.StartsWith("application/vnd.google-apps.") && kind == "file" && g is null) continue;
                result.Add(new DriveItem(f["id"]!.GetValue<string>(), f["name"]!.GetValue<string>(), kind, g));
            }
            pageToken = json["nextPageToken"]?.GetValue<string>();
        } while (pageToken is not null);
        return result;
    }

    [GeneratedRegex("""<a href="(?<href>[^"]+)"[^>]*>.*?flip-entry-title">(?<title>[^<]*)<""", RegexOptions.Singleline)]
    private static partial Regex EntryPattern();

    [GeneratedRegex(@"/(?:folders|d)/(?<id>[A-Za-z0-9_-]{10,})")]
    private static partial Regex IdPattern();

    /// <summary>Lists a public folder without an API key, using Drive's embeddable folder view.</summary>
    private async Task<List<DriveItem>> ScrapeListAsync(string folderId, CancellationToken ct)
    {
        var html = await http.GetStringAsync($"https://drive.google.com/embeddedfolderview?id={folderId}", ct);
        var result = new List<DriveItem>();
        foreach (Match m in EntryPattern().Matches(html))
        {
            var href = WebUtility.HtmlDecode(m.Groups["href"].Value);
            var title = WebUtility.HtmlDecode(m.Groups["title"].Value).Trim();
            var id = IdPattern().Match(href);
            if (!id.Success) continue;
            var kind = href.Contains("/folders/") ? "folder" : "file";
            var g = href.Contains("docs.google.com/document") ? "document"
                : href.Contains("docs.google.com/presentation") ? "presentation"
                : null;
            if (href.Contains("docs.google.com") && g is null) continue; // sheets, forms…
            result.Add(new DriveItem(id.Groups["id"].Value, title, kind, g));
        }
        return result;
    }

    // ---------- download + create ----------

    private string DownloadUrl(DriveItem item)
    {
        var key = google.Value.ApiKey;
        if (item.GoogleType is { } g)
        {
            return string.IsNullOrWhiteSpace(key)
                ? g == "document"
                    ? $"https://docs.google.com/document/d/{item.Id}/export?format=pdf"
                    : $"https://docs.google.com/presentation/d/{item.Id}/export/pdf"
                : $"https://www.googleapis.com/drive/v3/files/{item.Id}/export?mimeType=application/pdf&key={key}";
        }
        return string.IsNullOrWhiteSpace(key)
            ? $"https://drive.usercontent.google.com/download?id={item.Id}&export=download&confirm=t"
            : $"https://www.googleapis.com/drive/v3/files/{item.Id}?alt=media&key={key}";
    }

    private async Task ImportAsync(Planned p, string refKey, Mapping m, Guid uploaderId, CancellationToken ct)
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
            foreach (var (item, name) in p.Items)
            {
                i++;
                var part = p.Items.Count > 1 ? $"{i:D3}_{Storage.SafeName(name)}" : Storage.SafeName(name);
                var path = Path.Combine(dir, part);
                if (item.LocalPath is not null)
                    File.Copy(item.LocalPath, path, overwrite: true);
                else
                using (var res = await http.GetAsync(DownloadUrl(item), HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    res.EnsureSuccessStatusCode();
                    if (res.Content.Headers.ContentType?.MediaType == "text/html")
                        throw new InvalidOperationException($"Drive returned a web page instead of {name} (not public, or quota hit).");
                    await using var src = await res.Content.ReadAsStreamAsync(ct);
                    await using var dst = File.Create(path);
                    await src.CopyToAsync(dst, ct);
                }
                await using (var f = File.OpenRead(path)) hashes.Add(await SHA256.HashDataAsync(f, ct));
                size += new FileInfo(path).Length;
                parts.Add(part);
            }
        }
        catch
        {
            Storage.TryDeleteDir(dir);
            throw;
        }

        var hash = Convert.ToHexString(hashes.Count == 1 ? hashes[0] : SHA256.HashData(hashes.SelectMany(h => h).ToArray())).ToLowerInvariant();
        if (await db.Files.AnyAsync(f => f.Sha256 == hash, ct))
        {
            Storage.TryDeleteDir(dir);
            log.LogInformation("Already have {Title} (same content)", p.Title);
            return;
        }

        var first = p.Items[0].Path;
        var kind = DocumentConverter.IsImage(first) ? SourceKinds.Images : DocumentConverter.IsOffice(first) ? SourceKinds.Office : SourceKinds.Pdf;
        db.Files.Add(new DocFile
        {
            Id = id,
            CourseId = m.Course!.Id,
            MaterialType = m.Type,
            AcademicYear = m.Year,
            AssessmentLabel = m.Label,
            Folder = m.Folder,
            Title = p.Title,
            OriginalName = Path.GetFileName(first),
            SourceKind = kind,
            Parts = parts.ToArray(),
            Sha256 = hash,
            SourceRef = refKey,
            SizeBytes = size,
            Status = FileStatuses.Queued,
            UploadedById = uploaderId,
        });
        JobQueue.Enqueue(db, id, JobKinds.Process);
        await db.SaveChangesAsync(ct);
    }
}
