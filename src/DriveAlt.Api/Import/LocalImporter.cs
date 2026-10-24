using System.Text.Json;
using System.Text.Json.Nodes;
using DriveAlt.Api.Ai;
using DriveAlt.Api.Data;
using DriveAlt.Api.Processing;
using Microsoft.EntityFrameworkCore;

namespace DriveAlt.Api.Import;

/// <summary>
/// Fills pages of files already imported from the Drive with results prepared locally, so they don't need the page-reading
/// model:
/// - typed PDF pages get their PDF text layer (`results/&lt;driveId&gt;.json` from DriveAlt-local's process.mjs), read like the
///   offline fallback (assessment pages get one whole-page question box for now);
/// - scanned pages get reviewed question boxes (`crops/&lt;id4&gt;_p&lt;page&gt;.json`, pixel bboxes on the render sizes in
///   `crops/worklist.json`).
/// Pages already read are left alone. Files whose pages are now all read are queued to finish (embed, crop, ready).
/// Usage: dotnet DriveAlt.Api.dll import-local &lt;dir&gt; [--only "semester 1/"] [--skip "semester 1/AI/ITS"]... [--dry-run]
/// </summary>
public sealed class LocalImporter(AppDb db, ILogger<LocalImporter> log)
{
    public sealed record CropRegion(string? QuestionNumber, double[] Bbox, string Text);
    public sealed record CropPage(string Kind, List<CropRegion> Regions, int RenderWidth, int RenderHeight);

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (args.Length == 0 || args[0].StartsWith("--")) { log.LogError("Usage: import-local <dir> [--only <prefix>] [--skip <prefix>]... [--dry-run]"); return 2; }
        var dir = args[0];
        var dryRun = args.Contains("--dry-run");
        var only = Values(args, "--only").Select(p => p.ToLowerInvariant()).ToList();
        var skip = Values(args, "--skip").Select(p => p.ToLowerInvariant()).ToList();

        var paths = new Dictionary<string, string>();
        foreach (var f in JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(dir, "manifest.json"), ct))!["files"]!.AsArray())
            if (f?["id"] is JsonValue id && f["path"] is JsonValue p) paths[id.GetValue<string>()] = p.GetValue<string>();
        var crops = LoadCrops(Path.Combine(dir, "crops"));
        log.LogInformation("Loaded {Paths} manifest entries and {Crops} reviewed scanned pages", paths.Count, crops.Count);

        var files = await db.Files.Include(f => f.Course).Include(f => f.Pages).ThenInclude(p => p.Regions)
            .Where(f => f.SourceRef != null && f.SourceRef.StartsWith("gdrive:")).ToListAsync(ct);
        int touched = 0, fromText = 0, fromCrops = 0, complete = 0, stillNeedsAi = 0, notSplit = 0;
        foreach (var file in files)
        {
            var ids = file.SourceRef![7..].Split('+');
            if (ids.Length != 1 || !paths.TryGetValue(ids[0], out var path)) continue; // combined photos have no local results
            var lower = path.ToLowerInvariant();
            if ((only.Count > 0 && !only.Any(lower.StartsWith)) || skip.Any(lower.StartsWith)) continue;
            if (file.Pages.Count == 0) { notSplit++; continue; }

            var text = LoadText(Path.Combine(dir, "results", ids[0] + ".json"));
            var changed = 0;
            foreach (var page in file.Pages.Where(p => !p.Extracted && p.EditedAt is null).OrderBy(p => p.Number))
            {
                var ctx = new FileContext(file.Course?.Name ?? "", file.MaterialType, file.Title, file.AcademicYear, file.AssessmentLabel, page.Number, file.PageCount);
                PageExtraction x;
                if (crops.TryGetValue((ids[0], page.Number), out var crop))
                {
                    var regions = ToRegions(crop);
                    var pageText = string.Join("\n", crop.Regions.Select(r => r.Text).Where(t => !string.IsNullOrWhiteSpace(t)));
                    x = MaterialAi.Offline(ctx, pageText) with
                    {
                        Kind = PageKinds.Normalize(crop.Kind),
                        Regions = regions,
                        Summary = $"Page {page.Number} of {file.Title}: scanned page, question boxes reviewed before import.",
                        RawJson = "{\"localImport\":\"reviewed-crops\"}",
                    };
                    fromCrops++;
                }
                else if (text.TryGetValue(page.Number, out var t))
                {
                    x = MaterialAi.Offline(ctx, t) with
                    {
                        Summary = $"Page {page.Number} of {file.Title}: text from the PDF; question boxes not split yet.",
                        RawJson = "{\"localImport\":\"pdf-text\"}",
                    };
                    fromText++;
                }
                else continue;
                ProcessingService.Apply(file, page, x);
                page.Error = null;
                changed++;
            }

            if (changed == 0) continue;
            touched++;
            var allRead = file.Pages.All(p => p.Extracted);
            if (allRead) complete++; else stillNeedsAi++;
            file.Progress = file.Pages.Count(p => p.Extracted);
            file.UpdatedAt = DateTime.UtcNow;
            if (allRead && !dryRun) await QueueFinishAsync(file, ct);
            if (!dryRun) await db.SaveChangesAsync(ct);
            log.LogInformation("{Action} {Title}: {Changed} page(s) filled, {Left} still need the AI", dryRun ? "Would fill" : "Filled",
                file.Title, changed, file.Pages.Count(p => !p.Extracted));
        }
        log.LogInformation("Done{Dry}: {Touched} files, {Text} pages from PDF text, {Crops} scanned pages from reviewed crops; " +
            "{Complete} files fully read (queued to finish), {Partial} still need the AI for some pages, {NotSplit} not split yet.",
            dryRun ? " (dry run, nothing saved)" : "", touched, fromText, fromCrops, complete, stillNeedsAi, notSplit);
        return 0;
    }

    /// <summary>Lets the file's process job run now (it only embeds and crops, since every page is read), or queues one.</summary>
    private async Task QueueFinishAsync(DocFile file, CancellationToken ct)
    {
        var job = await db.Jobs.Where(j => j.FileId == file.Id && j.Kind == JobKinds.Process && j.State == "queued").FirstOrDefaultAsync(ct);
        if (job is not null) { job.RunAfter = DateTime.UtcNow; job.Error = null; }
        else
        {
            if (await db.Jobs.AnyAsync(j => j.FileId == file.Id && j.Kind == JobKinds.Process && j.State == "running", ct)) return;
            JobQueue.Enqueue(db, file.Id, JobKinds.Process);
        }
        file.Status = FileStatuses.Queued;
        file.Error = null;
    }

    /// <summary>Pixel boxes on the render → app regions (fractions of the page), clamped and padded like any model's boxes.</summary>
    internal static List<ExtractedRegion> ToRegions(CropPage page)
    {
        var list = new List<ExtractedRegion>();
        foreach (var r in page.Regions)
        {
            if (r.Bbox.Length != 4 || page.RenderWidth <= 0 || page.RenderHeight <= 0) continue;
            double X(double v) => Math.Clamp(v / page.RenderWidth, 0, 1);
            double Y(double v) => Math.Clamp(v / page.RenderHeight, 0, 1);
            var (x0, x1) = (Math.Min(X(r.Bbox[0]), X(r.Bbox[2])), Math.Max(X(r.Bbox[0]), X(r.Bbox[2])));
            var (y0, y1) = (Math.Min(Y(r.Bbox[1]), Y(r.Bbox[3])), Math.Max(Y(r.Bbox[1]), Y(r.Bbox[3])));
            if (x1 - x0 < 0.01 || y1 - y0 < 0.01) continue;
            var p = MaterialAi.BoxPadding;
            (x0, y0, x1, y1) = (Math.Max(0, x0 - p), Math.Max(0, y0 - p), Math.Min(1, x1 + p), Math.Min(1, y1 + p));
            list.Add(new ExtractedRegion(RegionLabels.Question, (float)x0, (float)y0, (float)(x1 - x0), (float)(y1 - y0), r.QuestionNumber, r.Text));
        }
        return list;
    }

    internal static Dictionary<(string Id, int Page), CropPage> LoadCrops(string dir)
    {
        var result = new Dictionary<(string, int), CropPage>();
        var worklist = Path.Combine(dir, "worklist.json");
        if (!File.Exists(worklist)) return result;
        var sizes = new Dictionary<(string, int), (int W, int H)>();
        foreach (var w in JsonNode.Parse(File.ReadAllText(worklist))!.AsArray())
            if (w is not null) sizes[(w.Str("id") ?? "", w.Int("page") ?? 0)] = (w.Int("w") ?? 0, w.Int("h") ?? 0);
        foreach (var f in Directory.EnumerateFiles(dir, "*_p*.json"))
        {
            var j = JsonNode.Parse(File.ReadAllText(f));
            if (j is null || j.Str("drive_id") is not { } id || j.Int("page") is not { } n || !sizes.TryGetValue((id, n), out var size)) continue;
            var regions = (j["regions"] as JsonArray ?? []).OfType<JsonNode>().Select(r => new CropRegion(
                r.Str("question_number"),
                (r["bbox"] as JsonArray ?? []).Select(v => v?.GetValue<double>() ?? 0).ToArray(),
                r.Str("text") ?? "")).ToList();
            result[(id, n)] = new CropPage(j.Str("page_kind") ?? PageKinds.Other, regions, size.W, size.H);
        }
        return result;
    }

    /// <summary>Page number → text for pages whose PDF text layer is usable (status "text_ready").</summary>
    private static Dictionary<int, string> LoadText(string path)
    {
        var result = new Dictionary<int, string>();
        if (!File.Exists(path)) return result;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var p in doc.RootElement.GetProperty("pages").EnumerateArray())
            if (p.GetProperty("status").GetString() == "text_ready")
                result[p.GetProperty("number").GetInt32()] = Clean(p.GetProperty("text").GetString() ?? "");
        return result;
    }

    /// <summary>Drops control characters some PDF fonts produce for math symbols (they break nothing but read as noise).</summary>
    internal static string Clean(string text) => new(text.Where(c => c is '\n' or '\t' || !char.IsControl(c)).ToArray());

    private static IEnumerable<string> Values(string[] args, string name) =>
        args.Select((a, i) => (a, i)).Where(x => x.a == name && x.i + 1 < args.Length).Select(x => args[x.i + 1]);
}
