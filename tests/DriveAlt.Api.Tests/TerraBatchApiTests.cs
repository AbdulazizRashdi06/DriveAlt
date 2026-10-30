using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DriveAlt.Api.Ai;
using DriveAlt.Api.Data;
using DriveAlt.Api.Files;
using DriveAlt.Api.Processing;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace DriveAlt.Api.Tests;

public partial class ApiTests
{
    [Fact]
    public async Task Terra_batches_checkpoint_before_a_limit_and_resume_only_unread_pages()
    {
        var client = await app.SignInAsync(ApiFactory.Uploader);
        var id = await BatchFileAsync(9);
        var calls = new List<int[]>();
        var limited = true;
        app.Material.BatchHandler = (pages, _) =>
        {
            calls.Add(pages.Select(p => p.Context.PageNumber).ToArray());
            if (limited && pages[0].Context.PageNumber == 9) throw new AiUnavailableException("Test quota limit");
            return Task.FromResult<IReadOnlyList<PageExtraction>>(pages.Select(p => MaterialAi.Offline(p.Context, "Question about queues")).ToList());
        };
        try
        {
            (await client.PostAsync($"/api/files/{id}/process", null)).EnsureSuccessStatusCode();
            await app.DrainJobsAsync();
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDb>();
                Assert.Equal(8, await db.Pages.CountAsync(p => p.FileId == id && p.Extracted));
                var job = await db.Jobs.SingleAsync(j => j.FileId == id);
                Assert.Equal("queued", job.State);
                Assert.StartsWith("Paused:", job.Error);
                Assert.Equal(0, job.Attempts);
                job.RunAfter = DateTime.UtcNow.AddSeconds(-1);
                await db.SaveChangesAsync();
            }
            limited = false;
            await app.DrainJobsAsync();
            Assert.Equal([1,2,3,4,5,6,7,8], calls[0]);
            Assert.Equal([9], calls[1]);
            Assert.Equal([9], calls[2]);
            var detail = await client.GetFromJsonAsync<JsonObject>($"/api/files/{id}");
            Assert.Equal("ready", detail!["file"]!["status"]!.GetValue<string>());
            Assert.Equal(9, detail["pages"]!.AsArray().Count);
        }
        finally { app.Material.BatchHandler = null; }
    }

    [Fact]
    public async Task Flagged_Terra_pages_keep_their_boxes_and_do_not_stop_the_file()
    {
        var client = await app.SignInAsync(ApiFactory.Uploader);
        var id = await BatchFileAsync(2);
        var calls = 0;
        app.Material.BatchHandler = (pages, _) =>
        {
            calls++;
            return Task.FromResult<IReadOnlyList<PageExtraction>>(pages.Select(p =>
                MaterialAi.Offline(p.Context, "Question about queues") with { NeedsReview = p.Context.PageNumber == 1 }).ToList());
        };
        try
        {
            (await client.PostAsync($"/api/files/{id}/process", null)).EnsureSuccessStatusCode();
            await app.DrainJobsAsync();
            Assert.Equal(1, calls);
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDb>();
                Assert.Equal(2, await db.Pages.CountAsync(p => p.FileId == id && p.Extracted));
                var flagged = await db.Pages.Include(p => p.Regions).SingleAsync(p => p.FileId == id && p.Number == 1);
                Assert.NotEmpty(flagged.Regions);
                Assert.Equal(ProcessingService.ReviewFlag, flagged.Error);
            }
            var detail = await client.GetFromJsonAsync<JsonObject>($"/api/files/{id}");
            Assert.Equal("ready", detail!["file"]!["status"]!.GetValue<string>());
            var inspect = await client.GetFromJsonAsync<JsonObject>($"/api/inspect/files/{id}");
            var page1 = inspect!["pages"]!.AsArray().Single(p => p!["number"]!.GetValue<int>() == 1)!;
            Assert.Equal(ProcessingService.ReviewFlag, page1["error"]!.GetValue<string>());
            (await client.PutAsJsonAsync($"/api/inspect/pages/{page1["id"]!.GetValue<string>()}", new { text = "Question about queues", kind = "assessment" })).EnsureSuccessStatusCode();
            inspect = await client.GetFromJsonAsync<JsonObject>($"/api/inspect/files/{id}");
            Assert.Null(inspect!["pages"]!.AsArray().Single(p => p!["number"]!.GetValue<int>() == 1)!["error"]);
        }
        finally { app.Material.BatchHandler = null; }
    }

    private async Task<Guid> BatchFileAsync(int count)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var storage = scope.ServiceProvider.GetRequiredService<Storage>();
        var file = new DocFile { Title = "Terra batch fixture", OriginalName = "paper.jpg", SourceKind = SourceKinds.Images,
            Parts = ["paper.jpg"], Status = FileStatuses.Uploaded, MaterialType = MaterialTypes.PastPaper,
            PageCount = count, Sha256 = Guid.NewGuid().ToString(),
            CourseId = (await db.Courses.FirstAsync()).Id,
            UploadedById = (await db.Users.SingleAsync(u => u.UniId == ApiFactory.Uploader)).Id };
        for (var n = 1; n <= count; n++) file.Pages.Add(new Page { File = file, Number = n, Width = 100, Height = 140 });
        db.Files.Add(file);
        await db.SaveChangesAsync();
        Directory.CreateDirectory(storage.PageDir(file.Id));
        using var bitmap = new SKBitmap(100,140);
        using var image = SKImage.FromBitmap(bitmap);
        using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg,80);
        for (var n = 1; n <= count; n++) await File.WriteAllBytesAsync(storage.PageImage(file.Id,n),jpeg.ToArray());
        return file.Id;
    }
}
