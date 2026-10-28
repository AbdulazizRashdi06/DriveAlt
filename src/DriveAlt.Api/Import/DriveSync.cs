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
            while (queue.TryDequeue(out var entry))
            {
                ct.ThrowIfCancellationRequested();
                if (!visited.Add(entry.Id)) continue;
                if (visited.Count > 2000 || entry.Segments.Length > 20)
                    throw new InvalidOperationException("Drive folder limit reached.");
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
                    var items = await importer.ListFolderAsync(entry.Id, ct);
                    folder.CheckedAt = DateTime.UtcNow;
                    foreach (var child in items.Where(i => i.Kind == "folder").OrderBy(i => i.Name))
                        queue.Enqueue((child.Id, entry.Id, child.Name, [.. entry.Segments, child.Name.Trim()]));
                    var fresh = items.Where(i => DriveImporter.CanImport(i) && !seenFiles.Contains(i.Id))
                        .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
                    var photos = fresh.Where(i => DocumentConverter.IsImage(i.Name)).ToList();
                    var batches = fresh.Except(photos).Select(i => new List<DriveImporter.DriveItem> { i }).ToList();
                    if (photos.Count > 0) batches.Add(photos);
                    foreach (var batch in batches)
                    {
                        // Read current switches immediately before starting each download, without stale tracked values.
                        var on = await db.DriveSyncStates.AsNoTracking().Where(s => s.Id == 1 && s.Enabled)
                            .Select(s => s.UploaderId).FirstOrDefaultAsync(ct);
                        if (on is not { } uploader || !await db.Users.AnyAsync(u => u.Id == uploader && u.Role == Roles.Uploader, ct)
                            || !await db.DriveSyncFolders.AsNoTracking().AnyAsync(f => f.Id == folder.Id && f.Enabled, ct)) break;
                        var fileId = await importer.ImportNewAsync(batch, entry.Segments, uploader, ct);
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
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // Never persist request URLs: a Google API key can be present in an exception message.
                    log.LogWarning("Drive sync failed in folder {Folder}; exception type {Type}", folder.Id, e.GetType().Name);
                    folder.Error = "Couldn't read or import this folder. Check sharing and download access; it will retry next scan.";
                    failed = true;
                    // ImportAsync may have attached entities before a failed save. Do not save partial imports here.
                    db.ChangeTracker.Clear();
                    folder = await db.DriveSyncFolders.SingleAsync(f => f.Id == entry.Id, ct);
                    folder.Error = "Couldn't read or import this folder. Check sharing and download access; it will retry next scan.";
                    state = await db.DriveSyncStates.SingleAsync(s => s.Id == 1, ct);
                }
                await db.SaveChangesAsync(ct);
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
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/admin/drive-sync").RequireAuthorization(AuthEndpoints.UploaderPolicy);
        g.MapGet("/", async (DriveSyncService sync, AppDb db, CancellationToken ct) =>
        {
            var state = await sync.StateAsync(ct);
            var folders = await db.DriveSyncFolders.AsNoTracking().ToListAsync(ct);
            return Results.Ok(new { state.Enabled, state.ScanRequested, state.StartedAt, state.FinishedAt, state.Error, state.Imported,
                folders = folders.OrderBy(f => string.Join('/', f.Segments), StringComparer.OrdinalIgnoreCase) });
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
        g.MapPut("/folders/{id}", async (string id, ToggleBody body, AppDb db, CancellationToken ct) =>
        {
            var folder = await db.DriveSyncFolders.FindAsync([id], ct);
            if (folder is null) return Results.NotFound();
            folder.Enabled = body.Enabled;
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });
    }
}
