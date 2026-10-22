using DriveAlt.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace DriveAlt.Api.Files;

/// <summary>
/// Disk layout under Storage:Root. Originals are written once and never modified.
///   originals/{fileId}/{part}       uploaded files (source of truth)
///   pdf/{fileId}.pdf                PDF converted from Office files
///   pages/{fileId}/{n}.jpg          rendered page (unmasked)
///   pages/{fileId}/{n}.masked.jpg   page with answer regions hatched out
///   thumbs/{fileId}/{n}.jpg         small masked thumbnail
///   crops/{regionId}.jpg            question crop (cut from the masked page)
/// </summary>
public sealed class Storage
{
    public string Root { get; }

    public Storage(IOptions<StorageOptions> opts, IWebHostEnvironment env)
    {
        Root = Path.IsPathRooted(opts.Value.Root) ? opts.Value.Root : Path.Combine(env.ContentRootPath, opts.Value.Root);
        foreach (var d in new[] { "originals", "pdf", "pages", "thumbs", "crops", "tmp" })
            Directory.CreateDirectory(Path.Combine(Root, d));
    }

    public string OriginalDir(Guid fileId) => Path.Combine(Root, "originals", fileId.ToString("N"));
    public string Original(Guid fileId, string part) => Path.Combine(OriginalDir(fileId), part);
    public string Pdf(Guid fileId) => Path.Combine(Root, "pdf", fileId.ToString("N") + ".pdf");
    public string PageDir(Guid fileId) => Path.Combine(Root, "pages", fileId.ToString("N"));
    public string PageImage(Guid fileId, int n) => Path.Combine(PageDir(fileId), $"{n}.jpg");
    public string MaskedPage(Guid fileId, int n) => Path.Combine(PageDir(fileId), $"{n}.masked.jpg");
    public string ThumbDir(Guid fileId) => Path.Combine(Root, "thumbs", fileId.ToString("N"));
    public string Thumb(Guid fileId, int n) => Path.Combine(ThumbDir(fileId), $"{n}.jpg");
    public string Crop(Guid regionId) => Path.Combine(Root, "crops", regionId.ToString("N") + ".jpg");

    public string TempDir()
    {
        var dir = Path.Combine(Root, "tmp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Removes everything derived from a file (keeps nothing but the DB rows the caller deletes).</summary>
    public void DeleteFile(Guid fileId, IEnumerable<Guid> regionIds)
    {
        TryDeleteDir(OriginalDir(fileId));
        TryDeleteDir(PageDir(fileId));
        TryDeleteDir(ThumbDir(fileId));
        TryDelete(Pdf(fileId));
        foreach (var r in regionIds) TryDelete(Crop(r));
    }

    public void DeleteDerived(Guid fileId)
    {
        TryDeleteDir(PageDir(fileId));
        TryDeleteDir(ThumbDir(fileId));
    }

    public static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
    }

    public static void TryDeleteDir(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch (IOException) { }
    }

    /// <summary>File-name safe version of an uploaded name (keeps the extension).</summary>
    public static string SafeName(string name)
    {
        var baseName = Path.GetFileName(name);
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(baseName.Select(c => invalid.Contains(c) || c == ' ' ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "file" : cleaned.Length > 120 ? cleaned[^120..] : cleaned;
    }
}
