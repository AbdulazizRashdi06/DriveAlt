using DriveAlt.Api.Ai;
using DriveAlt.Api.Data;
using DriveAlt.Api.Files;
using DriveAlt.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pgvector;

namespace DriveAlt.Api.Processing;

/// <summary>
/// The upload pipeline: split → read (AI extraction) → tag (embeddings) → crop questions and answers → ready.
/// Each stage saves its progress, so a retried job resumes where it stopped.
/// </summary>
public sealed class ProcessingService(
    AppDb db,
    Storage storage,
    DocumentConverter converter,
    PageRenderer renderer,
    IMaterialAi ai,
    IOptions<StorageOptions> storageOpts,
    IOptions<ProcessingOptions> procOpts,
    ILogger<ProcessingService> log)
{
    public async Task ProcessAsync(Guid fileId, CancellationToken ct)
    {
        var file = await db.Files.Include(f => f.Course).Include(f => f.Pages).ThenInclude(p => p.Regions)
            .FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (file is null) return;

        if (file.Pages.Count == 0)
        {
            await SetStatus(file, FileStatuses.Splitting, ct);
            await SplitAsync(file, ct);
        }

        await SetStatus(file, FileStatuses.Reading, ct);
        await ReadAsync(file, ct);

        await SetStatus(file, FileStatuses.Tagging, ct);
        await EmbedAsync(file, file.Pages, ct);

        await SetStatus(file, FileStatuses.Cropping, ct);
        foreach (var page in file.Pages.OrderBy(p => p.Number))
            await renderer.RenderAsync(file.Id, page, ct);

        file.Error = file.Pages.Any(p => p.Error is not null)
            ? $"{file.Pages.Count(p => p.Error is not null)} page(s) were read without AI (fallback). Check them in Inspect."
            : null;
        await SetStatus(file, FileStatuses.Ready, ct);
    }

    private async Task SetStatus(DocFile file, string status, CancellationToken ct)
    {
        file.Status = status;
        file.UpdatedAt = DateTime.UtcNow;
        if (status is FileStatuses.Splitting or FileStatuses.Reading) file.Progress = file.Pages.Count(p => p.Extracted);
        await db.SaveChangesAsync(ct);
    }

    // ---------- 1. split ----------

    private async Task SplitAsync(DocFile file, CancellationToken ct)
    {
        storage.DeleteDerived(file.Id);
        Directory.CreateDirectory(storage.PageDir(file.Id));
        var maxPages = storageOpts.Value.MaxPagesPerFile;
        var tmp = storage.TempDir();
        try
        {
            var pageFiles = new List<string>();
            switch (file.SourceKind)
            {
                case SourceKinds.Images:
                    var n = 0;
                    foreach (var part in file.Parts.Take(maxPages))
                    {
                        n++;
                        await converter.PrepareImageAsync(storage.Original(file.Id, part), storage.PageImage(file.Id, n), ct);
                        pageFiles.Add(storage.PageImage(file.Id, n));
                    }
                    break;
                case SourceKinds.Office:
                    var converted = await converter.OfficeToPdfAsync(storage.Original(file.Id, file.Parts[0]), tmp, ct);
                    File.Move(converted, storage.Pdf(file.Id), overwrite: true);
                    pageFiles = await MovePages(await converter.RenderPdfAsync(storage.Pdf(file.Id), Path.Combine(tmp, "r"), maxPages, ct), file.Id);
                    break;
                default:
                    pageFiles = await MovePages(await converter.RenderPdfAsync(storage.Original(file.Id, file.Parts[0]), Path.Combine(tmp, "r"), maxPages, ct), file.Id);
                    break;
            }
            if (pageFiles.Count == 0) throw new InvalidOperationException("The file has no pages.");

            for (var i = 0; i < pageFiles.Count; i++)
            {
                var (w, h) = ImageOps.Identify(pageFiles[i]);
                file.Pages.Add(new Page { FileId = file.Id, Number = i + 1, Width = w, Height = h });
            }
            file.PageCount = pageFiles.Count;
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            Storage.TryDeleteDir(tmp);
        }
    }

    private Task<List<string>> MovePages(List<string> rendered, Guid fileId)
    {
        var result = new List<string>(rendered.Count);
        for (var i = 0; i < rendered.Count; i++)
        {
            var dest = storage.PageImage(fileId, i + 1);
            File.Move(rendered[i], dest, overwrite: true);
            result.Add(dest);
        }
        return Task.FromResult(result);
    }

    /// <summary>PDF used for the text layer and the "open original" viewer, if the file has one.</summary>
    public string? PdfPath(DocFile file) => file.SourceKind switch
    {
        SourceKinds.Pdf => storage.Original(file.Id, file.Parts[0]),
        SourceKinds.Office when File.Exists(storage.Pdf(file.Id)) => storage.Pdf(file.Id),
        _ => null,
    };

    // ---------- 2. read ----------

    private async Task ReadAsync(DocFile file, CancellationToken ct)
    {
        var courses = await db.Courses.AsNoTracking().OrderBy(c => c.Sort).ToListAsync(ct);
        var pdf = PdfPath(file);
        var pending = file.Pages.Where(p => !p.Extracted).OrderBy(p => p.Number).ToList();
        var gate = new SemaphoreSlim(1, 1);

        await Parallel.ForEachAsync(pending, new ParallelOptions { MaxDegreeOfParallelism = procOpts.Value.PageConcurrency, CancellationToken = ct },
            async (page, token) =>
            {
                var text = pdf is null ? "" : await converter.PdfPageTextAsync(pdf, page.Number, token);
                var ctx = new FileContext(file.Course!.Name, file.MaterialType, file.Title, file.AcademicYear, file.AssessmentLabel, page.Number, file.PageCount);
                PageExtraction extraction;
                string? error = null;
                try
                {
                    var jpeg = await File.ReadAllBytesAsync(storage.PageImage(file.Id, page.Number), token);
                    extraction = await ai.ExtractPageAsync(jpeg, ctx, text, courses, token);
                }
                // A usage limit or lost login pauses the whole file (see JobWorker) instead of reading pages offline.
                catch (Exception e) when (e is not OperationCanceledException && !AiUnavailableException.In(e))
                {
                    log.LogWarning(e, "AI extraction failed for {File} page {Page}; using offline fallback", file.Id, page.Number);
                    extraction = MaterialAi.Offline(ctx, text);
                    error = e.Message.Length > 500 ? e.Message[..500] : e.Message;
                }

                await gate.WaitAsync(token);
                try
                {
                    Apply(file, page, extraction);
                    page.Error = error;
                    file.Progress++;
                    file.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(token);
                }
                finally
                {
                    gate.Release();
                }
            });
    }

    public static void Apply(DocFile file, Page page, PageExtraction x)
    {
        page.Kind = x.Kind;
        page.Text = x.Text;
        page.AiSummary = x.Summary;
        page.Topic = x.Topic;
        page.Tags = x.Tags;
        page.AcademicYear = x.AcademicYear;
        page.RawExtraction = Json.Parse(x.RawJson) is null ? null : x.RawJson;
        page.Regions.Clear();
        var order = 0;
        foreach (var r in x.Regions.OrderBy(r => MathF.Round(r.Y, 2)).ThenBy(r => r.X))
        {
            page.Regions.Add(new Region
            {
                PageId = page.Id,
                Label = r.Label,
                X = r.X, Y = r.Y, W = r.W, H = r.H,
                QuestionNumber = r.QuestionNumber,
                Text = r.Text,
                Order = order++,
            });
        }
        page.Extracted = true;

        // Fill file-level facts the uploader left empty.
        file.AcademicYear ??= x.AcademicYear;
        if (string.IsNullOrWhiteSpace(file.AssessmentLabel) && !string.IsNullOrWhiteSpace(x.AssessmentLabel))
            file.AssessmentLabel = x.AssessmentLabel;
        IndexText.Refresh(file, page);
    }

    // ---------- 3. tag ----------

    public async Task EmbedAsync(DocFile file, IEnumerable<Page> pages, CancellationToken ct)
    {
        var list = pages.ToList();
        foreach (var p in list) IndexText.Refresh(file, p);
        if (!ai.Online)
        {
            await db.SaveChangesAsync(ct);
            return;
        }

        var questions = list.SelectMany(p => p.Regions.Where(r => r.Label == RegionLabels.Question).Select(r => (p, r))).ToList();
        try
        {
            var pageVecs = await ai.EmbedAsync(list.Select(p => IndexText.EmbedPage(file, p)).ToList(), query: false, ct);
            for (var i = 0; i < list.Count; i++)
                if (pageVecs[i] is { } v) list[i].Embedding = new Vector(v);

            var regionVecs = await ai.EmbedAsync(questions.Select(x => IndexText.EmbedRegion(file, x.p, x.r)).ToList(), query: false, ct);
            for (var i = 0; i < questions.Count; i++)
                if (regionVecs[i] is { } v) questions[i].r.Embedding = new Vector(v);
        }
        catch (Exception e) when (e is GeminiException { Status: 429 } or HttpRequestException)
        {
            // Out of embedding quota (or Gemini unreachable): keep the file with keyword search instead of failing it.
            // A later Re-run AI or reembed fills the vectors in.
            log.LogWarning("Embeddings skipped for {File} ({Reason}); keyword search only for now", file.Id, e.Message.Length > 120 ? e.Message[..120] : e.Message);
        }

        foreach (var r in list.SelectMany(p => p.Regions).Where(r => r.Label != RegionLabels.Question))
            r.Embedding = null;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>After an uploader edit or a metadata change: refresh search text and embeddings.</summary>
    public async Task ReembedAsync(Guid fileId, Guid? pageId, CancellationToken ct)
    {
        var file = await db.Files.Include(f => f.Course).Include(f => f.Pages.Where(p => pageId == null || p.Id == pageId))
            .ThenInclude(p => p.Regions).FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (file is null) return;
        await EmbedAsync(file, file.Pages, ct);
    }

    // ---------- upload suggestion ----------

    /// <summary>Quick look at page 1 to pre-fill course/type/year for the uploader to confirm.</summary>
    public async Task SuggestAsync(Guid fileId, CancellationToken ct)
    {
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (file is null || file.Status != FileStatuses.Uploaded || !ai.Online || file.Guessed.Length == 0) return;
        var courses = await db.Courses.AsNoTracking().OrderBy(c => c.Sort).ToListAsync(ct);
        var tmp = storage.TempDir();
        try
        {
            string firstPage;
            string? text = null;
            if (file.SourceKind == SourceKinds.Images)
            {
                firstPage = Path.Combine(tmp, "1.jpg");
                await converter.PrepareImageAsync(storage.Original(file.Id, file.Parts[0]), firstPage, ct);
            }
            else
            {
                var pdf = file.SourceKind == SourceKinds.Office
                    ? await converter.OfficeToPdfAsync(storage.Original(file.Id, file.Parts[0]), tmp, ct)
                    : storage.Original(file.Id, file.Parts[0]);
                firstPage = (await converter.RenderPdfAsync(pdf, Path.Combine(tmp, "r"), 1, ct))[0];
                text = await converter.PdfPageTextAsync(pdf, 1, ct);
            }
            var s = await ai.SuggestAsync(await File.ReadAllBytesAsync(firstPage, ct), file.OriginalName, text, courses, ct);
            if (s is null) return;

            await db.Entry(file).ReloadAsync(ct); // the uploader may have confirmed meanwhile
            if (file.Status != FileStatuses.Uploaded) return;
            var guessed = file.Guessed.ToHashSet();
            if (guessed.Contains("course") && courses.FirstOrDefault(c => c.Slug == s.CourseSlug) is { } c) file.CourseId = c.Id;
            if (guessed.Contains("type") && s.MaterialType is { } t) file.MaterialType = t;
            if (file.AcademicYear is null && s.AcademicYear is { } y) { file.AcademicYear = y; guessed.Add("year"); }
            if (string.IsNullOrWhiteSpace(file.AssessmentLabel) && s.AssessmentLabel is { } l) { file.AssessmentLabel = l; guessed.Add("label"); }
            file.Guessed = guessed.ToArray();
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            Storage.TryDeleteDir(tmp);
        }
    }
}
