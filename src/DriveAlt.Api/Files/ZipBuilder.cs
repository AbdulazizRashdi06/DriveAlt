using System.IO.Compression;

namespace DriveAlt.Api.Files;

/// <summary>
/// Builds a ZIP in a temp file (ZipArchive writes synchronously, which Kestrel forbids on the response stream)
/// and returns it as a stream that deletes the file when closed.
/// </summary>
public static class ZipBuilder
{
    public static async Task<Stream> BuildAsync(IEnumerable<(string Path, string EntryName)> files, CancellationToken ct)
    {
        var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "drivealt-" + Guid.NewGuid().ToString("N") + ".zip");
        var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: true))
            {
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (path, name) in files)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!File.Exists(path)) continue;
                    var entry = name;
                    var stem = System.IO.Path.GetFileNameWithoutExtension(name);
                    var ext = System.IO.Path.GetExtension(name);
                    for (var n = 2; !used.Add(entry); n++) entry = $"{stem} ({n}){ext}";
                    await zip.CreateEntryFromFileAsync(path, entry, CompressionLevel.NoCompression, ct);
                }
            }
            fs.Position = 0;
            return fs;
        }
        catch
        {
            await fs.DisposeAsync();
            throw;
        }
    }
}
