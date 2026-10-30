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
        app.Drive.Folders["ds-folder"] = [.. app.Drive.Folders["ds-folder"], new("disabled-file", "disabled.pdf", "file", null)];
        await admin.PostAsJsonAsync("/api/admin/drive-sync/scan", new { });
        db.ChangeTracker.Clear();
        await sync.RunOnceAsync(default);
        Assert.Equal(2, app.Drive.Downloads.Count);
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

    [Fact]
    public async Task One_failing_drive_file_does_not_hide_the_rest_of_its_folder()
    {
        var admin = await app.SignInAsync(ApiFactory.Uploader);
        var photos = Enumerable.Range(0, DriveSyncService.PhotosPerFile + 1)
            .Select(n => new DriveImporter.DriveItem($"bulk-photo-{n:D2}", $"photo {n:D2}.jpg", "file", null)).ToArray();
        app.Drive.Folders[DriveImporter.Year2FolderId] = [new("bad-sem", "semester 1", "folder", null)];
        app.Drive.Folders["bad-sem"] = [new("bad-ds", "Data Structures", "folder", null)];
        app.Drive.Folders["bad-ds"] = [new("a-broken", "a broken.pdf", "file", null), new("b-good", "b good.pdf", "file", null), .. photos];
        app.Drive.FailDownloads.Add("a-broken");
        try
        {
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var sync = scope.ServiceProvider.GetRequiredService<DriveSyncService>();
            await admin.PostAsJsonAsync("/api/admin/drive-sync/scan", new { });
            await sync.RunOnceAsync(default); // discovers the folders, all off
            await admin.PutAsJsonAsync("/api/admin/drive-sync/folders/bad-ds", new { enabled = true });
            await admin.PutAsJsonAsync("/api/admin/drive-sync/", new { enabled = true });
            db.ChangeTracker.Clear();
            await sync.RunOnceAsync(default);

            var items = await db.DriveSyncItems.AsNoTracking().Where(i => i.FolderId == "bad-ds").ToListAsync();
            Assert.Contains(items, i => i.Id == "b-good");
            Assert.DoesNotContain(items, i => i.Id == "a-broken");
            Assert.Equal(photos.Length, items.Count(i => i.Id.StartsWith("bulk-photo-")));
            // Photos are grouped in chunks, so the joined source ref stays inside the index's row limit.
            Assert.Equal(2, items.Where(i => i.Id.StartsWith("bulk-photo-")).Select(i => i.FileId).Distinct().Count());
            var folder = await db.DriveSyncFolders.AsNoTracking().SingleAsync(f => f.Id == "bad-ds");
            Assert.Contains("couldn't be imported", folder.Error);
        }
        finally
        {
            app.Drive.FailDownloads.Clear();
            await admin.PutAsJsonAsync("/api/admin/drive-sync/", new { enabled = false });
        }
    }

    [Fact]
    public async Task Folder_files_are_listed_with_their_upload_status()
    {
        var admin = await app.SignInAsync(ApiFactory.Uploader);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await app.SignInAsync("24-7711")).GetAsync("/api/admin/drive-sync/folders/list-ds/files")).StatusCode);
        app.Drive.Folders[DriveImporter.Year2FolderId] = [new("list-sem", "semester 1", "folder", null)];
        app.Drive.Folders["list-sem"] = [new("list-ds", "Data Structures", "folder", null)];
        app.Drive.Folders["list-ds"] = [new("list-sub", "Quizzes", "folder", null), new("list-legacy", "b legacy.pdf", "file", null),
            new("list-new", "c new.pdf", "file", null), new("list-video", "a lecture.mp4", "file", null),
            new("list-manual", "d Quiz 3 2024 (1).pdf", "file", null), new("list-other-course", "e Algorithms notes.pdf", "file", null)];
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var uploader = await db.Users.SingleAsync(u => u.UniId == ApiFactory.Uploader);
        var legacy = new DocFile { Title = "Legacy import", OriginalName = "b legacy.pdf", SourceKind = SourceKinds.Pdf,
            Sha256 = Guid.NewGuid().ToString(), SourceRef = "gdrive:list-legacy", UploadedById = uploader.Id };
        db.Files.Add(legacy);
        // Uploaded by hand: no Drive ID, matched by name within the folder's course only.
        var ds = await db.Courses.SingleAsync(c => c.Slug == "data-structures");
        var other = await db.Courses.FirstAsync(c => c.Slug != "data-structures");
        var manual = new DocFile { Title = "D Quiz 3 2024", OriginalName = "d quiz 3 2024.pdf", CourseId = ds.Id,
            Sha256 = Guid.NewGuid().ToString(), UploadedById = uploader.Id };
        db.Files.Add(manual);
        db.Files.Add(new DocFile { Title = "e Algorithms notes", OriginalName = "e Algorithms notes.pdf", CourseId = other.Id,
            Sha256 = Guid.NewGuid().ToString(), UploadedById = uploader.Id });
        await db.SaveChangesAsync();
        await admin.PostAsJsonAsync("/api/admin/drive-sync/scan", new { });
        await scope.ServiceProvider.GetRequiredService<DriveSyncService>().RunOnceAsync(default);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/admin/drive-sync/folders/unknown/files")).StatusCode);
        var files = (await admin.GetFromJsonAsync<JsonArray>("/api/admin/drive-sync/folders/list-ds/files"))!;
        Assert.Equal(["a lecture.mp4", "b legacy.pdf", "c new.pdf", "d Quiz 3 2024 (1).pdf", "e Algorithms notes.pdf"],
            files.Select(f => f!["name"]!.GetValue<string>()));
        Assert.Equal(manual.Id.ToString(), files[3]!["fileId"]!.GetValue<string>());
        Assert.Null(files[4]!["fileId"]); // same name, but filed under another course
        Assert.False(files[0]!["importable"]!.GetValue<bool>());
        Assert.Equal(legacy.Id.ToString(), files[1]!["fileId"]!.GetValue<string>());
        Assert.Null(files[2]!["fileId"]);
        Assert.True(files[2]!["importable"]!.GetValue<bool>());
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
