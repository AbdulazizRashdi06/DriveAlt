using System.Diagnostics;
using System.Text.RegularExpressions;
using DriveAlt.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace DriveAlt.Api.Processing;

/// <summary>Wraps LibreOffice, poppler and libheif to turn originals into page images.</summary>
public sealed partial class DocumentConverter(IOptions<ProcessingOptions> opts, ILogger<DocumentConverter> log)
{
    public const int MaxLongSide = 2000;
    private ProcessingOptions O => opts.Value;

    public static readonly string[] OfficeExtensions = [".pptx", ".ppt", ".docx", ".doc", ".odp", ".odt", ".pps", ".ppsx"];
    public static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp", ".heic", ".heif", ".gif", ".bmp", ".tif", ".tiff"];

    public static bool IsOffice(string name) => OfficeExtensions.Contains(Path.GetExtension(name).ToLowerInvariant());
    public static bool IsImage(string name) => ImageExtensions.Contains(Path.GetExtension(name).ToLowerInvariant());
    public static bool IsPdf(string name) => Path.GetExtension(name).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
    public static bool IsSupported(string name) => IsPdf(name) || IsOffice(name) || IsImage(name);

    /// <summary>Converts an Office document to PDF and returns the PDF path inside <paramref name="outDir"/>.</summary>
    public async Task<string> OfficeToPdfAsync(string input, string outDir, CancellationToken ct)
    {
        var profile = Path.Combine(Path.GetTempPath(), "lo-" + Guid.NewGuid().ToString("N"));
        try
        {
            await RunAsync(O.SofficePath,
                [$"-env:UserInstallation=file://{profile}", "--headless", "--norestore", "--convert-to", "pdf", "--outdir", outDir, input],
                TimeSpan.FromMinutes(5), ct);
        }
        finally
        {
            try { Directory.Delete(profile, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        var pdf = Path.Combine(outDir, Path.GetFileNameWithoutExtension(input) + ".pdf");
        if (!File.Exists(pdf)) throw new InvalidOperationException("LibreOffice did not produce a PDF.");
        return pdf;
    }

    [GeneratedRegex(@"-(\d+)\.jpg$")]
    private static partial Regex PageSuffix();

    /// <summary>Renders each PDF page to a JPEG. Returns paths in page order.</summary>
    public async Task<List<string>> RenderPdfAsync(string pdf, string outDir, int maxPages, CancellationToken ct)
    {
        Directory.CreateDirectory(outDir);
        await RunAsync(O.PdftoppmPath,
            ["-jpeg", "-jpegopt", "quality=85", "-scale-to", MaxLongSide.ToString(), "-l", maxPages.ToString(), pdf, Path.Combine(outDir, "p")],
            TimeSpan.FromMinutes(10), ct);
        return Directory.GetFiles(outDir, "p-*.jpg")
            .Select(f => (f, n: int.Parse(PageSuffix().Match(f).Groups[1].Value)))
            .OrderBy(x => x.n).Select(x => x.f).ToList();
    }

    /// <summary>Text layer of one PDF page ("" for scans).</summary>
    public async Task<string> PdfPageTextAsync(string pdf, int page, CancellationToken ct)
    {
        try
        {
            var text = await RunAsync(O.PdftotextPath, ["-f", page.ToString(), "-l", page.ToString(), "-layout", "-enc", "UTF-8", pdf, "-"],
                TimeSpan.FromMinutes(1), ct);
            return WhitespaceRuns().Replace(text, " ").Trim();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogDebug(e, "pdftotext failed for {Pdf} p{Page}", pdf, page);
            return "";
        }
    }

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex WhitespaceRuns();

    /// <summary>Normalizes a photo/image: HEIC → JPEG, EXIF auto-rotate, cap the size.</summary>
    public async Task<(int Width, int Height)> PrepareImageAsync(string input, string output, CancellationToken ct)
    {
        var source = input;
        string? temp = null;
        var ext = Path.GetExtension(input).ToLowerInvariant();
        if (ext is ".heic" or ".heif")
        {
            temp = Path.Combine(Path.GetDirectoryName(output)!, Guid.NewGuid().ToString("N") + ".jpg");
            await RunAsync(O.HeifConvertPath, [input, temp], TimeSpan.FromMinutes(2), ct);
            source = temp;
        }
        try
        {
            return await Task.Run(() =>
            {
                using var upright = ImageOps.LoadUpright(source);
                using var fitted = ImageOps.FitWithin(upright, MaxLongSide, MaxLongSide);
                ImageOps.SaveJpeg(fitted, output, 85);
                return (fitted.Width, fitted.Height);
            }, ct);
        }
        finally
        {
            if (temp is not null) File.Delete(temp);
        }
    }

    private static async Task<string> RunAsync(string file, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {file}");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var stdout = p.StandardOutput.ReadToEndAsync(cts.Token);
        var stderr = p.StandardError.ReadToEndAsync(cts.Token);
        try
        {
            await p.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch (InvalidOperationException) { }
            throw new TimeoutException($"{Path.GetFileName(file)} timed out");
        }
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(file)} exited with {p.ExitCode}: {(await stderr).Trim()}");
        return await stdout;
    }
}
