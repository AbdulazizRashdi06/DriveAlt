using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DriveAlt.Api.Data;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace DriveAlt.Api.Tests;

public partial class ApiTests
{
    [Fact]
    public async Task Pages_without_embeddings_are_shown_as_not_embedded()
    {
        var client = await app.SignInAsync(ApiFactory.Uploader);
        static Vector Vec() { var v = new float[AppDb.EmbeddingDimensions]; v[0] = 1; return new Vector(v); }
        static Region Box(string label, Vector? emb) => new() { Label = label, W = 0.5f, H = 0.5f, Embedding = emb };
        static Page MakePage(int n, bool extracted, Vector? emb, params Region[] regions) =>
            new() { Number = n, Width = 100, Height = 100, Extracted = extracted, Embedding = emb, Regions = [.. regions] };

        Guid fileId;
        Dictionary<int, Guid> pageIds;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var owner = (await db.Users.SingleAsync(u => u.UniId == ApiFactory.Uploader)).Id;
            var file = new DocFile
            {
                Title = "Embedding status", OriginalName = "embedding-status.pdf", Sha256 = Guid.NewGuid().ToString(),
                UploadedById = owner, CourseId = (await db.Courses.FirstAsync()).Id, Status = FileStatuses.Ready, PageCount = 4,
                Pages =
                [
                    // Fully embedded. An answer box never gets a vector, so it must not count against the page.
                    MakePage(1, true, Vec(), Box(RegionLabels.Question, Vec()), Box(RegionLabels.Answer, null)),
                    // No page vector.
                    MakePage(2, true, null),
                    // Page vector, but a question box without one.
                    MakePage(3, true, Vec(), Box(RegionLabels.Question, null)),
                    // Not read yet: it isn't expected to have a vector, so it isn't counted.
                    MakePage(4, false, null),
                ],
            };
            db.Files.Add(file);
            await db.SaveChangesAsync();
            fileId = file.Id;
            pageIds = file.Pages.ToDictionary(p => p.Number, p => p.Id);
        }
        try
        {
            var listed = (await client.GetFromJsonAsync<JsonArray>("/api/uploads?all=true"))!
                .Single(f => f!["id"]!.GetValue<string>() == fileId.ToString())!;
            Assert.Equal(2, listed["unembedded"]!.GetValue<int>());

            var inspect = (await client.GetFromJsonAsync<JsonObject>($"/api/inspect/files/{fileId}"))!;
            var embedded = inspect["pages"]!.AsArray().ToDictionary(p => p!["number"]!.GetValue<int>(), p => p!["embedded"]!.GetValue<bool>());
            Assert.True(embedded[1]);
            Assert.False(embedded[2]);
            Assert.False(embedded[3]);

            // Once every read page is embedded, the file stops reporting any.
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            foreach (var p in await db.Pages.Include(p => p.Regions).Where(p => p.FileId == fileId && p.Extracted).ToListAsync())
            {
                p.Embedding ??= Vec();
                foreach (var r in p.Regions.Where(r => r.Label == RegionLabels.Question)) r.Embedding ??= Vec();
            }
            await db.SaveChangesAsync();
            var after = (await client.GetFromJsonAsync<JsonArray>("/api/uploads?all=true"))!
                .Single(f => f!["id"]!.GetValue<string>() == fileId.ToString())!;
            Assert.Equal(0, after["unembedded"]!.GetValue<int>());
        }
        finally
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            await db.Files.Where(f => f.Id == fileId).ExecuteDeleteAsync();
        }
    }
}
