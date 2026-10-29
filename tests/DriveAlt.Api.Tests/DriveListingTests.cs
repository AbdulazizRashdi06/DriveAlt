using DriveAlt.Api.Import;

namespace DriveAlt.Api.Tests;

public class DriveListingTests
{
    [Fact]
    public async Task Listings_overlap_with_a_bound_and_keep_results_in_input_order()
    {
        var running = 0;
        var peak = 0;
        var results = await DriveSyncService.ListFoldersAsync(Enumerable.Range(0, 12).Select(i => i.ToString()).ToList(),
            async (id, ct) =>
            {
                var active = Interlocked.Increment(ref running);
                int old;
                do { old = peak; } while (active > old && Interlocked.CompareExchange(ref peak, active, old) != old);
                try { await Task.Delay(20, ct); return [new DriveImporter.DriveItem(id, id, "folder", null)]; }
                finally { Interlocked.Decrement(ref running); }
            }, default);
        Assert.InRange(peak, 2, DriveSyncService.ListingConcurrency);
        Assert.Equal(Enumerable.Range(0, 12).Select(i => i.ToString()), results.Select(r => Assert.Single(r.Items!).Id));
    }

    [Fact]
    public async Task A_slow_folder_times_out_without_discarding_other_results()
    {
        var results = await DriveSyncService.ListFoldersAsync(["slow", "fast"], async (id, ct) =>
        {
            if (id == "slow") await Task.Delay(Timeout.Infinite, ct);
            return [];
        }, default, TimeSpan.FromMilliseconds(40));
        Assert.IsAssignableFrom<OperationCanceledException>(results[0].Error);
        Assert.Null(results[1].Error);
        Assert.NotNull(results[1].Items);
    }

    [Fact]
    public async Task Stopping_the_scan_propagates_cancellation()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DriveSyncService.ListFoldersAsync(["folder"],
            (_, ct) => Task.FromResult(new List<DriveImporter.DriveItem>()), stop.Token));
    }
}
