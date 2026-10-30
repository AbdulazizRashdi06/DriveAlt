using System.Net;
using System.Net.Http.Json;
using System.Text;
using DriveAlt.Api.Import;

namespace DriveAlt.Api.Tests;

public sealed class ScriptedDrive : HttpMessageHandler
{
    public Dictionary<string, DriveImporter.DriveItem[]> Folders { get; } = [];
    public List<string> Downloads { get; } = [];
    public HashSet<string> FailFolders { get; } = [];
    public HashSet<string> FailDownloads { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var url = request.RequestUri!;
        if (url.AbsolutePath == "/drive/v3/files")
        {
            var query = Uri.UnescapeDataString(url.Query);
            var start = query.IndexOf('\'') + 1;
            var id = query[start..query.IndexOf('\'', start)];
            if (FailFolders.Contains(id)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            var files = Folders.GetValueOrDefault(id, []).Select(i => new { id = i.Id, name = i.Name,
                mimeType = i.Kind == "folder" ? "application/vnd.google-apps.folder" : "application/pdf" });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { files }) });
        }
        var fileId = url.Segments.Last();
        Downloads.Add(fileId);
        if (FailDownloads.Contains(fileId)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF-1.4 test content " + fileId)),
        });
    }
}
