using System.IO.Compression;
using DriveAlt.Api.Import;

namespace DriveAlt.Api.Tests;

public class SniffTests
{
    private static string Temp(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1 }, ".jpg")]
    [InlineData(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 0, 0, 0, 13 }, ".png")]
    [InlineData(new byte[] { (byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-', (byte)'1', (byte)'.', (byte)'7', 10, 0, 0, 0 }, ".pdf")]
    [InlineData(new byte[] { 0, 0, 0, 24, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'h', (byte)'e', (byte)'i', (byte)'c' }, ".heic")]
    [InlineData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }, null)]
    public void Drive_names_without_extension_get_one_from_their_content(byte[] head, string? expected)
    {
        var path = Temp(head);
        try { Assert.Equal(expected, DriveImporter.SniffExtension(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Office_zips_are_told_apart_by_their_parts()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create)) zip.CreateEntry("word/document.xml");
        try { Assert.Equal(".docx", DriveImporter.SniffExtension(path)); }
        finally { File.Delete(path); }
    }
}
