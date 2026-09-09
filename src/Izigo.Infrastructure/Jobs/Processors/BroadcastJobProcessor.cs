using Izigo.Application.Common.Interfaces;
using Izigo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Izigo.Infrastructure.Jobs.Processors;

/// <summary>
/// Fires a scheduled broadcast. Hangfire calls this at the time stored in Broadcast.ScheduledAt.
/// </summary>
public class BroadcastJobProcessor(
    ApplicationDbContext db,
    IPushService push,
    IConfiguration config,
    ILogger<BroadcastJobProcessor> logger)
{
    public async Task ProcessAsync(string jobId, CancellationToken ct)
    {
        var job = await db.BackgroundJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null || job.Status != "queued") return;

        job.Status   = "running";
        await db.SaveChangesAsync(ct);

        try
        {
            // The job's Type encodes the broadcast id as "broadcast:{broadcastId}"
            var broadcastId = job.Type.Replace("broadcast:", "");
            var broadcast   = await db.Broadcasts.FirstOrDefaultAsync(b => b.Id == broadcastId, ct);

            if (broadcast is null || broadcast.IsCancelled)
            {
                job.Status      = "completed";
                job.CompletedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                return;
            }

            if (broadcast.Channel == "push")
            {
                var cleanupDays = int.TryParse(config["Jobs:BroadcastCleanupDays"], out var cd) ? cd : 30;
                var tokens = await ResolveTokensAsync(broadcast.Audience, broadcast.Market, db, ct, cleanupDays);

                if (tokens.Count > 0)
                {
                    await push.SendBatchAsync(tokens, broadcast.Title, broadcast.Body,
                        "broadcast", broadcast.DeepLink, ct: ct);
                    broadcast.SentAt         = DateTime.UtcNow;
                    broadcast.DeliveredCount = tokens.Count;
                }
            }

            job.Status      = "completed";
            job.Progress    = 100;
            job.CompletedAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Broadcast] Job {JobId} failed", jobId);
            job.Status = "failed";
            job.Error  = ex.Message;
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task<List<string>> ResolveTokensAsync(string audience, string market,
        ApplicationDbContext db, CancellationToken ct, int cleanupDays = 30)
    {
        var cutoff = DateTime.UtcNow.AddDays(-cleanupDays);
        return audience switch
        {
            "all_riders" => await (
                from d in db.UserDevices
                join u in db.Users on d.UserId equals u.Id
                where u.Role == Domain.Enums.UserRole.Rider &&
                      u.Status == Domain.Enums.UserStatus.Active && d.FcmToken != null
                select d.FcmToken!).Distinct().ToListAsync(ct),

            "all_drivers" => await (
                from d in db.UserDevices
                join dp in db.DriverProfiles on d.UserId equals dp.UserId
                where dp.KycStatus == Domain.Enums.KycStatus.Approved && d.FcmToken != null
                select d.FcmToken!).Distinct().ToListAsync(ct),

            "online_drivers" => await (
                from d in db.UserDevices
                join dp in db.DriverProfiles on d.UserId equals dp.UserId
                where dp.IsOnline && d.FcmToken != null
                select d.FcmToken!).Distinct().ToListAsync(ct),

            "inactive_riders" => await (
                from d in db.UserDevices
                join u in db.Users on d.UserId equals u.Id
                where u.Role == Domain.Enums.UserRole.Rider && d.FcmToken != null &&
                      !db.Trips.Any(t => t.RiderId == u.Id && t.CreatedAt >= cutoff)
                select d.FcmToken!).Distinct().ToListAsync(ct),

            _ => []
        };
    }
}
