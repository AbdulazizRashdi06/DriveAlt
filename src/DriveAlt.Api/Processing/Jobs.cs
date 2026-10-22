using DriveAlt.Api.Data;
using DriveAlt.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DriveAlt.Api.Processing;

public static class JobQueue
{
    public static void Enqueue(AppDb db, Guid fileId, string kind, Guid? pageId = null) =>
        db.Jobs.Add(new Job { FileId = fileId, Kind = kind, PageId = pageId });

    /// <summary>Claims the next due job (Postgres SKIP LOCKED, safe with several workers/instances).</summary>
    public static async Task<Job?> ClaimAsync(AppDb db, CancellationToken ct)
    {
        var ids = await db.Database.SqlQueryRaw<long>("""
            UPDATE jobs SET state = 'running', locked_at = now(), attempts = attempts + 1
            WHERE id = (
                SELECT id FROM jobs WHERE state = 'queued' AND run_after <= now()
                ORDER BY id FOR UPDATE SKIP LOCKED LIMIT 1)
            RETURNING id
            """).ToListAsync(ct);
        return ids.Count == 0 ? null : await db.Jobs.FindAsync([ids[0]], ct);
    }
}

/// <summary>Background workers that run upload processing jobs.</summary>
public sealed class JobWorker(IServiceScopeFactory scopes, IOptions<ProcessingOptions> opts, ILogger<JobWorker> log) : BackgroundService
{
    public const int MaxAttempts = 3;

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            // Jobs left running by a crashed/restarted process go back on the queue.
            await db.Database.ExecuteSqlRawAsync("UPDATE jobs SET state = 'queued' WHERE state = 'running'", stop);
        }
        var workers = Enumerable.Range(0, Math.Max(1, opts.Value.Workers)).Select(i => RunLoop(i, stop));
        await Task.WhenAll(workers);
    }

    private async Task RunLoop(int worker, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            bool worked;
            try
            {
                worked = await RunOnceAsync(stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                log.LogError(e, "Worker {Worker} loop error", worker);
                worked = false;
            }
            if (!worked)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(1.5), stop); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    /// <summary>Runs one job if one is due. Returns false when the queue was empty.</summary>
    public async Task<bool> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var job = await JobQueue.ClaimAsync(db, ct);
        if (job is null) return false;

        var processing = scope.ServiceProvider.GetRequiredService<ProcessingService>();
        try
        {
            switch (job.Kind)
            {
                case JobKinds.Suggest: await processing.SuggestAsync(job.FileId, ct); break;
                case JobKinds.Process: await processing.ProcessAsync(job.FileId, ct); break;
                case JobKinds.Reembed: await processing.ReembedAsync(job.FileId, job.PageId, ct); break;
                default: throw new InvalidOperationException($"Unknown job kind {job.Kind}");
            }
            job.State = "done";
            job.Error = null;
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log.LogError(e, "Job {Job} ({Kind}) for file {File} failed (attempt {Attempt})", job.Id, job.Kind, job.FileId, job.Attempts);
            job.Error = e.Message.Length > 2000 ? e.Message[..2000] : e.Message;
            var retry = job.Attempts < MaxAttempts && job.Kind != JobKinds.Suggest;
            job.State = retry ? "queued" : "failed";
            if (retry) job.RunAfter = DateTime.UtcNow.AddSeconds(20 * job.Attempts);

            // Drop half-applied entity changes; progress already saved by the pipeline is kept.
            db.ChangeTracker.Clear();
            db.Jobs.Attach(job).State = EntityState.Modified;
            if (!retry && job.Kind == JobKinds.Process)
            {
                var file = await db.Files.FindAsync([job.FileId], CancellationToken.None);
                if (file is not null)
                {
                    file.Status = FileStatuses.Failed;
                    file.Error = job.Error;
                    file.UpdatedAt = DateTime.UtcNow;
                }
            }
        }
        await db.SaveChangesAsync(CancellationToken.None);
        return true;
    }
}
