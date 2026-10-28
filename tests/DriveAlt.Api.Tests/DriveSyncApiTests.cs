using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DriveAlt.Api.Data;
using DriveAlt.Api.Import;
using Microsoft.EntityFrameworkCore;

namespace DriveAlt.Api.Tests;

public partial class ApiTests
{
    [Fact]
    public async Task Drive_sync_is_opt_in_for_each_folder_and_never_releases_held_jobs()
    {
        var student = await app.SignInAsync("24-7711");
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync("/api/admin/drive-sync/")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.PostAsJsonAsync("/api/admin/drive-sync/scan", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.PutAsJsonAsync("/api/admin/drive-sync/", new { enabled = true })).StatusCode);
        var admin = await app.SignInAsync(ApiFactory.Uploader);
        var state = await admin.GetFromJsonAsync<JsonObject>("/api/admin/drive-sync/");
        Assert.False(state!["enabled"]!.GetValue<bool>());
        app.Drive.Folders[DriveImporter.Year2FolderId] = [new("sem-one", "semester 1", "folder", null), new("sem-two", "semester 2", "folder", null)];
        app.Drive.Folders["sem-one"] = [new("ds-folder", "Data Structures", "folder", null), new("its-folder", "ITS", "folder", null)];
        app.Drive.Folders["sem-two"] = [new("later-file", "later.pdf", "file", null)];
        app.Drive.Folders["ds-folder"] = [new("old-photo", "old.jpg", "file", null), new("new-photo", "new.jpg", "file", null), new("new-pdf", "paper.pdf", "file", null)];
        app.Drive.Folders["its-folder"] = [new("its-file", "ITS.pdf", "file", null)];

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var uploader = await db.Users.SingleAsync(u => u.UniId == ApiFactory.Uploader);
        var old = new DocFile { Title = "Existing photo group", OriginalName = "old.jpg", SourceKind = SourceKinds.Images,
            Sha256 = Guid.NewGuid().ToString(), SourceRef = "gdrive:old-photo+another-old-photo", UploadedById = uploader.Id };
        db.Files.Add(old);
        var held = new Job { FileId = old.Id, Kind = "process", RunAfter = new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc), Error = "Held: owner scope" };
        db.Jobs.Add(held);
        await db.SaveChangesAsync();
        var sync = scope.ServiceProvider.GetRequiredService<DriveSyncService>();
        await admin.PostAsJsonAsync("/api/admin/drive-sync/scan", new { });
        await sync.RunOnceAsync(default);
        Assert.Empty(app.Drive.Downloads);
        Assert.All(await db.DriveSyncFolders.AsNoTracking().ToListAsync(), f => Assert.False(f.Enabled));

        // Selecting a folder while globally paused still does not download anything.
        await admin.PutAsJsonAsync("/api/admin/drive-sync/folders/ds-folder", new { enabled = true });
        await admin.PostAsJsonAsync("/api/admin/drive-sync/scan", new { });
        db.ChangeTracker.Clear();
        await sync.RunOnceAsync(default);
        Assert.Empty(app.Drive.Downloads);
        await admin.PutAsJsonAsync("/api/admin/drive-sync/", new { enabled = true });
        db.ChangeTracker.Clear();
        await sync.RunOnceAsync(default);
        Assert.Equal(new[] { "new-pdf", "new-photo" }, app.Drive.Downloads.Order().ToArray());
        Assert.Equal(2, await db.DriveSyncItems.CountAsync());
        Assert.Equal("Held: owner scope", (await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == held.Id)).Error);
        Assert.Equal(2100, (await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == held.Id)).RunAfter.Year);

        // Repeat scans must not redownload new or legacy grouped sources. New subfolders start off.
        app.Drive.Folders["ds-folder"] = [.. app.Drive.Folders["ds-folder"], new("new-subfolder", "More papers", "folder", null)];
        app.Drive.Folders["new-subfolder"] = [new("future-paper", "future.pdf", "file", null)];
        await admin.PostAsJsonAsync("/api/admin/drive-sync/scan", new { });
        db.ChangeTracker.Clear();
        await sync.RunOnceAsync(default);
        Assert.Equal(2, app.Drive.Downloads.Count);
        Assert.False((await db.DriveSyncFolders.FindAsync("new-subfolder"))!.Enabled);
        await admin.PutAsJsonAsync("/api/admin/drive-sync/folders/ds-folder", new { enabled = false });
        await admin.PutAsJsonAsync("/api/admin/drive-sync/", new { enabled = false });
    }

    [Fact]
    public async Task Failed_drive_listing_keeps_existing_files_and_reports_a_safe_retryable_error()
    {
        var admin = await app.SignInAsync(ApiFactory.Uploader);
        app.Drive.FailFolders.Add(DriveImporter.Year2FolderId);
        await admin.PostAsJsonAsync("/api/admin/drive-sync/scan", new { });
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var before = await db.Files.CountAsync();
        await scope.ServiceProvider.GetRequiredService<DriveSyncService>().RunOnceAsync(default);
        Assert.Equal(before, await db.Files.CountAsync());
        var state = await admin.GetFromJsonAsync<JsonObject>("/api/admin/drive-sync/");
        Assert.NotNull(state!["finishedAt"]);
        Assert.Contains("could not", state["error"]!.GetValue<string>());
        Assert.DoesNotContain("test-only", state.ToJsonString());
        app.Drive.FailFolders.Clear();
    }
}

public class DriveSyncTests
{
    [Fact]
    public void Legacy_grouped_photos_and_tracked_source_ids_are_deduplicated_individually()
    {
        Assert.Equal(new[] { "a", "b", "c" }, DriveSyncService.KnownIds(["gdrive:a+b", "gdrive:b", "other:ignored"], ["c"]).Order().ToArray());
    }
}
