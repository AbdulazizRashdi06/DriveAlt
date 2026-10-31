using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DriveAlt.Api.Data;
using DriveAlt.Api.Processing;
using Microsoft.EntityFrameworkCore;

namespace DriveAlt.Api.Tests;

public partial class ApiTests
{
    [Fact]
    public async Task Unfinished_uploads_say_why_they_are_waiting()
    {
        var client = await app.SignInAsync(ApiFactory.Uploader);
        var later = DateTime.UtcNow.AddMinutes(20);
        Guid Make(AppDb db, Guid owner, int courseId, string title, Job? job)
        {
            var file = new DocFile { Title = title, OriginalName = title + ".pdf", Sha256 = Guid.NewGuid().ToString(),
                UploadedById = owner, CourseId = courseId, Status = FileStatuses.Reading, PageCount = 2 };
            db.Files.Add(file);
            if (job is not null) { job.FileId = file.Id; db.Jobs.Add(job); }
            return file.Id;
        }
        Dictionary<string, Guid> ids;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var owner = (await db.Users.SingleAsync(u => u.UniId == ApiFactory.Uploader)).Id;
            var course = (await db.Courses.FirstAsync()).Id;
            ids = new()
            {
                ["reading"] = Make(db, owner, course, "Wait reading", new Job { Kind = JobKinds.Process, State = "running" }),
                ["paused"] = Make(db, owner, course, "Wait paused", new Job { Kind = JobKinds.Process, RunAfter = later, Error = "Paused: Codex unavailable (429)" }),
                ["retrying"] = Make(db, owner, course, "Wait retrying", new Job { Kind = JobKinds.Process, RunAfter = later, Attempts = 2, Error = "Incomplete extraction batch." }),
                ["held"] = Make(db, owner, course, "Wait held", new Job { Kind = JobKinds.Process, RunAfter = new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc), Error = "Held: owner scope" }),
                ["queued"] = Make(db, owner, course, "Wait queued", new Job { Kind = JobKinds.Process, RunAfter = DateTime.UtcNow.AddMinutes(-1) }),
                ["stuck"] = Make(db, owner, course, "Wait stuck", null),
            };
            await db.SaveChangesAsync();
        }
        try
        {
            var files = (await client.GetFromJsonAsync<JsonArray>("/api/uploads"))!.ToDictionary(f => f!["id"]!.GetValue<string>(), f => f!["wait"]);
            foreach (var (reason, id) in ids)
                Assert.Equal(reason, files[id.ToString()]!["reason"]!.GetValue<string>());
            Assert.Contains("429", files[ids["paused"].ToString()]!["detail"]!.GetValue<string>());
            Assert.NotNull(files[ids["paused"].ToString()]!["until"]);
            Assert.Equal(2, files[ids["retrying"].ToString()]!["attempt"]!.GetValue<int>());
            Assert.NotNull(files[ids["queued"].ToString()]!["ahead"]);

            // A held file can be released with Resume; it then waits in the normal queue.
            (await client.PostAsync($"/api/files/{ids["held"]}/resume", null)).EnsureSuccessStatusCode();
            Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.PostAsync($"/api/files/{ids["queued"]}/resume", null)).StatusCode);
            var after = (await client.GetFromJsonAsync<JsonArray>("/api/uploads"))!.Single(f => f!["id"]!.GetValue<string>() == ids["held"].ToString())!;
            Assert.Equal("queued", after["wait"]!["reason"]!.GetValue<string>());
            Assert.False((await app.SignInAsync("25-8894")).PostAsync($"/api/files/{ids["paused"]}/resume", null).Result.IsSuccessStatusCode);

            // A stuck file can be queued again with Retry.
            (await client.PostAsync($"/api/files/{ids["stuck"]}/process", null)).EnsureSuccessStatusCode();
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            Assert.True(await db.Jobs.AnyAsync(j => j.FileId == ids["stuck"] && j.State == "queued"));
        }
        finally
        {
            // Leave no runnable jobs behind for other tests' DrainJobsAsync.
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var all = ids.Values.ToList();
            await db.Jobs.Where(j => all.Contains(j.FileId)).ExecuteDeleteAsync();
            await db.Files.Where(f => all.Contains(f.Id)).ExecuteDeleteAsync();
        }
    }
}
