using DriveAlt.Api.Auth;
using DriveAlt.Api.Data;
using DriveAlt.Api.Files;
using DriveAlt.Api.Infrastructure;
using DriveAlt.Api.Processing;
using Microsoft.EntityFrameworkCore;

namespace DriveAlt.Api.Import;

public sealed class DriveSyncService(AppDb db, DriveImporter importer, ILogger<DriveSyncService> log)
{
    private const long LockId = 738129405;
    internal const int ListingConcurrency = 6;
    internal const int PhotosPerFile = 40;

    internal sealed record FolderListing(List<DriveImporter.DriveItem>? Items, Exception? Error);

    internal static async Task<FolderListing[]> ListFoldersAsync(IReadOnlyList<string> ids,
        Func<string, CancellationToken, Task<List<DriveImporter.DriveItem>>> list, CancellationToken ct,
        TimeSpan? folderTimeout = null)
    {
        using var gate = new SemaphoreSlim(ListingConcurrency);
        return await Task.WhenAll(ids.Select(async id =>
        {
            await gate.WaitAsync(ct);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(folderTimeout ?? TimeSpan.FromSeconds(20));
                try { return new FolderListing(await list(id, timeout.Token), null); }
                catch (Exception e) when (!ct.IsCancellationRequested) { return new FolderListing(null, e); }
            }
            finally { gate.Release(); }
        }));
    }

    public async Task<DriveSyncState> StateAsync(CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO drive_sync_states (id, enabled, scan_requested, imported)
            VALUES (1, false, false, 0) ON CONFLICT DO NOTHING
            """, ct);
        return await db.DriveSyncStates.SingleAsync(x => x.Id == 1, ct);
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        // Session lock prevents overlapping downloads across app replicas. Keep this connection until finally.
        await db.Database.OpenConnectionAsync(ct);
        var locked = false;
        try
        {
            locked = (await db.Database.SqlQueryRaw<bool>($"SELECT pg_try_advisory_lock({LockId}) AS \"Value\"").ToListAsync(ct)).Single();
            if (!locked) return;
            var state = await StateAsync(ct);
            if (!state.ScanRequested && (!state.Enabled || state.StartedAt > DateTime.UtcNow.AddMinutes(-5))) return;
            state.ScanRequested = false;
            state.StartedAt = DateTime.UtcNow;
            state.FinishedAt = null;
            state.Error = null;
            state.Imported = 0;
            await db.SaveChangesAsync(ct);

            var known = await db.DriveSyncItems.Select(x => x.Id).ToListAsync(ct);
            var oldRefs = await db.Files.Where(f => f.SourceRef != null && f.SourceRef.StartsWith("gdrive:"))
                .Select(f => f.SourceRef!).ToListAsync(ct);
            var seenFiles = KnownIds(oldRefs, known);
            var visited = new HashSet<string>();
            var queue = new Queue<(string Id, string? Parent, string Name, string[] Segments)>();
            queue.Enqueue((DriveImporter.Year2FolderId, null, "Year 2 Drive", []));
            var failed = false;
            while (queue.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var entries = new List<(string Id, string? Parent, string Name, string[] Segments)>();
                while (entries.Count < ListingConcurrency && queue.TryDequeue(out var next))
                {
                    if (!visited.Add(next.Id)) continue;
                    if (visited.Count > 2000 || next.Segments.Length > 20)
                        throw new InvalidOperationException("Drive folder limit reached.");
                    entries.Add(next);
                }
                // Only HTTP listings run concurrently. The scoped DbContext, imports and switch checks remain serial.
                var listings = await ListFoldersAsync(entries.Select(e => e.Id).ToList(), importer.ListFolderAsync, ct);
                for (var entryIndex = 0; entryIndex < entries.Count; entryIndex++)
                {
                    var entry = entries[entryIndex];
                    var folder = await db.DriveSyncFolders.FindAsync([entry.Id], ct);
                    if (folder is null)
                    {
                        folder = new DriveSyncFolder { Id = entry.Id, Name = entry.Name };
                        db.DriveSyncFolders.Add(folder);
                    }
                    folder.Name = entry.Name;
                    folder.ParentId = entry.Parent;
                    folder.Segments = entry.Segments;
                    folder.Available = true;
                    folder.Error = null;
                    await db.SaveChangesAsync(ct);
                    try
                    {
                        var listing = listings[entryIndex];
                        if (listing.Error is { } error) throw error;
                        var items = listing.Items!;
                        folder.CheckedAt = DateTime.UtcNow;
                        // Remember what this folder holds, so the panel can colour folders without listing Drive again.
                        await db.DriveSyncSeenFiles.Where(f => f.FolderId == entry.Id).ExecuteDeleteAsync(ct);
                        db.DriveSyncSeenFiles.AddRange(items.Where(i => i.Kind != "folder").DistinctBy(i => i.Id).Select(i =>
                            new DriveSyncSeenFile { FolderId = entry.Id, Id = i.Id, Name = i.Name, Importable = DriveImporter.CanImport(i) }));
                        await db.SaveChangesAsync(ct);
                        foreach (var child in items.Where(i => i.Kind == "folder").OrderBy(i => i.Name))
                            queue.Enqueue((child.Id, entry.Id, child.Name, [.. entry.Segments, child.Name.Trim()]));
                        var fresh = items.Where(i => DriveImporter.CanImport(i) && !seenFiles.Contains(i.Id))
                            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
                        var photos = fresh.Where(i => DocumentConverter.IsImage(i.Name)).ToList();
                        var batches = fresh.Except(photos).Select(i => new List<DriveImporter.DriveItem> { i }).ToList();
                        // New photos in one folder become one file, in chunks so the joined source ref stays well inside
                        // Postgres's btree row limit (~2.7 KB; Drive IDs are ~33 chars).
                        batches.AddRange(photos.Chunk(PhotosPerFile).Select(c => c.ToList()));
                        foreach (var batch in batches)
                        {
                            // Read current switches immediately before starting each download, without stale tracked values.
                            var on = await db.DriveSyncStates.AsNoTracking().Where(s => s.Id == 1 && s.Enabled)
                                .Select(s => s.UploaderId).FirstOrDefaultAsync(ct);
                            if (on is not { } uploader || !await db.Users.AnyAsync(u => u.Id == uploader && u.Role == Roles.Uploader, ct)
                                || !await IsActiveAsync(db, folder.Id, ct)) break;
                            Guid? fileId;
                            try { fileId = await importer.ImportNewAsync(batch, entry.Segments, uploader, ct); }
                            catch (Exception e) when (!ct.IsCancellationRequested)
                            {
                                // One bad item (undetectable content, a Drive 403, a download timeout) must not hide the
                                // rest of the folder: note it and carry on with the next batch.
                                log.LogWarning("Drive sync skipped an item in folder {Folder}; exception type {Type}", entry.Id, e.GetType().Name);
                                failed = true;
                                db.ChangeTracker.Clear();
                                folder = await db.DriveSyncFolders.SingleAsync(f => f.Id == entry.Id, ct);
                                folder.Error = "Some files in this folder couldn't be imported. Check they are PDF, Office or image files with download access; they will retry next scan.";
                                state = await db.DriveSyncStates.SingleAsync(s => s.Id == 1, ct);
                                await db.SaveChangesAsync(ct);
                                continue;
                            }
                            if (fileId is null)
                            {
                                folder.Error = "No matching course for this folder. Move material under a recognised course folder.";
                                failed = true;
                                break;
                            }
                            foreach (var item in batch)
                            {
                                db.DriveSyncItems.Add(new DriveSyncItem { Id = item.Id, FolderId = folder.Id, FileId = fileId.Value });
                                seenFiles.Add(item.Id);
                            }
                            state.Imported++;
                            await db.SaveChangesAsync(ct);
                        }
                    }
                    catch (Exception e) when (!ct.IsCancellationRequested)
                    {
                        // Never persist request URLs: a Google API key can be present in an exception message.
                        log.LogWarning("Drive sync failed in folder {Folder}; exception type {Type}", folder.Id, e.GetType().Name);
                        folder.Error = "Couldn't read or import this folder. Check sharing and download access; it will retry next scan.";
                        failed = true;
                        // ImportAsync may have attached entities before a failed save. Do not save partial imports here.
                        db.ChangeTracker.Clear();
                        folder = await db.DriveSyncFolders.SingleAsync(f => f.Id == entry.Id, ct);
                        folder.CheckedAt = DateTime.UtcNow;
                        folder.Error = "Couldn't read or import this folder. Check sharing and download access; it will retry next scan.";
                        state = await db.DriveSyncStates.SingleAsync(s => s.Id == 1, ct);
                    }
                    await db.SaveChangesAsync(ct);
                }
            }
            if (!failed)
                await db.DriveSyncFolders.Where(f => !visited.Contains(f.Id)).ExecuteUpdateAsync(s => s.SetProperty(f => f.Available, false), ct);
            // Update only status fields; a concurrent pause/request must never be overwritten by this scan.
            await db.DriveSyncStates.Where(s => s.Id == 1).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.FinishedAt, DateTime.UtcNow)
                .SetProperty(x => x.Error, failed ? "Some folders could not be synced. See folder details." : null), ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning("Drive sync stopped; exception type {Type}", e.GetType().Name);
            db.ChangeTracker.Clear();
            await db.DriveSyncStates.Where(s => s.Id == 1).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.FinishedAt, DateTime.UtcNow)
                .SetProperty(x => x.Error, "Drive sync could not finish. Check folder access and try again."), ct);
        }
        finally
        {
            if (locked) await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_unlock({LockId})", CancellationToken.None);
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>A folder syncs when its own switch is on, or a folder above it is on "with subfolders".</summary>
    public static async Task<bool> IsActiveAsync(AppDb db, string folderId, CancellationToken ct)
    {
        var current = await db.DriveSyncFolders.AsNoTracking().Where(f => f.Id == folderId)
            .Select(f => new { f.Enabled, f.ParentId }).FirstOrDefaultAsync(ct);
        if (current is null) return false;
        if (current.Enabled) return true;
        var parentId = current.ParentId;
        for (var depth = 0; parentId is not null && depth < 25; depth++)
        {
            var parent = await db.DriveSyncFolders.AsNoTracking().Where(f => f.Id == parentId)
                .Select(f => new { f.Enabled, f.Recursive, f.ParentId }).FirstOrDefaultAsync(ct);
            if (parent is null) return false;
            if (parent.Enabled && parent.Recursive) return true;
            parentId = parent.ParentId;
        }
        return false;
    }

    public static HashSet<string> KnownIds(IEnumerable<string> sourceRefs, IEnumerable<string> tracked) =>
        sourceRefs.Where(s => s.StartsWith("gdrive:")).SelectMany(s => s[7..].Split('+')).Concat(tracked).ToHashSet();
}

public sealed class DriveSyncWorker(IServiceScopeFactory scopes, ILogger<DriveSyncWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
                timeout.CancelAfter(TimeSpan.FromMinutes(15));
                await scope.ServiceProvider.GetRequiredService<DriveSyncService>().RunOnceAsync(timeout.Token);
                await Task.Delay(TimeSpan.FromSeconds(5), stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch (Exception e)
            {
                log.LogWarning("Drive sync worker will retry; exception type {Type}", e.GetType().Name);
                if (e is OperationCanceledException)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
                    await db.DriveSyncStates.Where(s => s.Id == 1).ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.FinishedAt, DateTime.UtcNow)
                        .SetProperty(x => x.Error, "The scan timed out. Please try again."), stop);
                }
                try { await Task.Delay(TimeSpan.FromSeconds(30), stop); }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            }
        }
    }
}

public static class DriveSyncEndpoints
{
    public sealed record ToggleBody(bool Enabled);
    /// <summary>A folder's switch: "off", "only" (its own files) or "all" (it and every folder below it).</summary>
    public sealed record FolderBody(bool? Enabled = null, string? Mode = null);

    /// <summary>
    /// Upload state of a folder and everything below it, from the files seen at the last scan: <c>missing</c> (some file
    /// isn't in the app), <c>working</c> (all are in, some still processing or failed), <c>done</c> (all in and ready)
    /// or <c>unknown</c> (no files seen yet).
    /// </summary>
    public sealed record FolderStatus(string State, int Files, int Missing, int Working, int Failed);

    internal static async Task<Dictionary<string, FolderStatus>> StatusesAsync(AppDb db, List<DriveSyncFolder> folders, CancellationToken ct)
    {
        var seen = await db.DriveSyncSeenFiles.AsNoTracking().Where(f => f.Importable).ToListAsync(ct);
        var matcher = await DriveUploadMatcher.LoadAsync(db, ct);
        var byId = folders.ToDictionary(f => f.Id);
        var own = new Dictionary<string, (int Files, int Missing, int Working, int Failed)>();
        foreach (var file in seen)
        {
            if (!byId.TryGetValue(file.FolderId, out var folder)) continue;
            var app = matcher.Find(folder.Segments, file.Id, file.Name);
            var c = own.GetValueOrDefault(file.FolderId);
            c.Files++;
            if (app is null) c.Missing++;
            else if (app.Status == FileStatuses.Failed) c.Failed++;
            else if (app.Status != FileStatuses.Ready) c.Working++;
            own[file.FolderId] = c;
        }
        // Each folder counts its own files plus every folder below it (by parent link), so a parent shows where files are missing.
        var children = folders.Where(f => f.ParentId is not null).GroupBy(f => f.ParentId!).ToDictionary(g => g.Key, g => g.Select(f => f.Id).ToList());
        var totals = new Dictionary<string, (int Files, int Missing, int Working, int Failed)>();
        (int Files, int Missing, int Working, int Failed) Total(string id, int depth)
        {
            if (totals.TryGetValue(id, out var done)) return done;
            var t = own.GetValueOrDefault(id);
            if (depth < 25)
                foreach (var child in children.GetValueOrDefault(id) ?? [])
                {
                    var c = Total(child, depth + 1);
                    t = (t.Files + c.Files, t.Missing + c.Missing, t.Working + c.Working, t.Failed + c.Failed);
                }
            return totals[id] = t;
        }
        var result = new Dictionary<string, FolderStatus>();
        foreach (var f in folders)
        {
            var t = Total(f.Id, 0);
            var state = t.Files == 0 ? "unknown" : t.Missing > 0 ? "missing" : t.Working + t.Failed > 0 ? "working" : "done";
            result[f.Id] = new FolderStatus(state, t.Files, t.Missing, t.Working, t.Failed);
        }
        return result;
    }
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/admin/drive-sync").RequireAuthorization(AuthEndpoints.UploaderPolicy);
        g.MapGet("/", async (DriveSyncService sync, AppDb db, CancellationToken ct) =>
        {
            var state = await sync.StateAsync(ct);
            var folders = await db.DriveSyncFolders.AsNoTracking().ToListAsync(ct);
            var statuses = await StatusesAsync(db, folders, ct);
            return Results.Ok(new { state.Enabled, state.ScanRequested, state.StartedAt, state.FinishedAt, state.Error, state.Imported,
                folders = folders.OrderBy(f => string.Join('/', f.Segments), StringComparer.OrdinalIgnoreCase).Select(f => new
                {
                    f.Id, f.ParentId, f.Name, f.Segments, f.Enabled, f.Recursive, f.Available, f.CheckedAt, f.Error,
                    Status = statuses.GetValueOrDefault(f.Id),
                }) });
        });
        g.MapPut("/", async (ToggleBody body, HttpContext http, DriveSyncService sync, AppDb db, CancellationToken ct) =>
        {
            var state = await sync.StateAsync(ct);
            state.Enabled = body.Enabled;
            state.UploaderId = http.User.RequiredUserId();
            if (body.Enabled) state.ScanRequested = true;
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });
        g.MapPost("/scan", async (DriveSyncService sync, AppDb db, CancellationToken ct) =>
        {
            var state = await sync.StateAsync(ct);
            state.ScanRequested = true;
            await db.SaveChangesAsync(ct);
            return Results.Accepted();
        });
        // A folder's own files, listed live from Drive, each marked uploaded (with its app file) or not.
        g.MapGet("/folders/{id}/files", async (string id, AppDb db, DriveImporter importer, ILogger<DriveSyncService> log, CancellationToken ct) =>
        {
            var folder = await db.DriveSyncFolders.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
            if (folder is null) return Results.NotFound();
            List<DriveImporter.DriveItem> items;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                items = await importer.ListFolderAsync(id, timeout.Token);
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                // Never return the exception text: a Google API key can be in its URL.
                log.LogWarning("Drive folder listing failed for {Folder}; exception type {Type}", id, e.GetType().Name);
                return Results.Problem("Couldn't list this folder on Drive. Check its sharing and try again.", statusCode: 502);
            }
            var files = items.Where(i => i.Kind != "folder").OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
            var matcher = await DriveUploadMatcher.LoadAsync(db, ct);
            return Results.Ok(files.Select(f =>
            {
                var app = matcher.Find(folder.Segments, f.Id, f.Name);
                return new { f.Id, f.Name, Importable = DriveImporter.CanImport(f), FileId = app?.Id, FileStatus = app?.Status };
            }));
        });
        g.MapPut("/folders/{id}", async (string id, FolderBody body, AppDb db, CancellationToken ct) =>
        {
            var folder = await db.DriveSyncFolders.FindAsync([id], ct);
            if (folder is null) return Results.NotFound();
            switch (body.Mode ?? (body.Enabled == true ? "only" : body.Enabled == false ? "off" : null))
            {
                case "off": folder.Enabled = false; folder.Recursive = false; break;
                case "only": folder.Enabled = true; folder.Recursive = false; break;
                case "all": folder.Enabled = true; folder.Recursive = true; break;
                default: return Results.BadRequest(new { error = "mode must be off, only or all." });
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });
    }
}
