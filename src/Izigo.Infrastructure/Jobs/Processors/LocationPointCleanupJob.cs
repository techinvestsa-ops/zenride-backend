using Izigo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Izigo.Infrastructure.Jobs.Processors;

/// <summary>
/// Nightly: deletes idle driver location points (heartbeats sent while online
/// but not on a trip) older than <c>Jobs:LocationPointRetentionDays</c>
/// (default 7). Each online driver adds one every few seconds, so without
/// this the table grows by millions of rows a month.
///
/// Points recorded on a trip (<c>JobId</c> set) are kept: support and safety
/// reviews use them to replay a route.
/// </summary>
public class LocationPointCleanupJob(
    ApplicationDbContext db,
    IConfiguration config,
    ILogger<LocationPointCleanupJob> logger)
{
    private const int BatchSize = 5_000;

    public async Task RunAsync(CancellationToken ct)
    {
        var days = config.GetValue("Jobs:LocationPointRetentionDays", 7);
        var cutoff = DateTime.UtcNow.AddDays(-days);
        var total = 0;

        // Small batches keep each delete short, so heartbeat inserts are not
        // blocked behind one long-running statement.
        while (!ct.IsCancellationRequested)
        {
            var deleted = await db.DriverLocationPoints
                .Where(p => p.JobId == null && p.RecordedAt < cutoff)
                .OrderBy(p => p.RecordedAt)
                .Take(BatchSize)
                .ExecuteDeleteAsync(ct);
            total += deleted;
            if (deleted < BatchSize) break;
        }

        logger.LogInformation(
            "[Cleanup] deleted {Count} idle driver location points older than {Days} days", total, days);
    }
}
