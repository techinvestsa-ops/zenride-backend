using Izigo.Application.Common.Interfaces;
using Izigo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Izigo.Infrastructure.Jobs.Processors;

/// <summary>
/// Executes report BackgroundJob records that were queued by POST /admin/reports/{key}/run.
/// Hangfire invokes this via the IBackgroundJobClient; it is NOT called on the HTTP request thread.
/// </summary>
public class ReportJobProcessor(
    ApplicationDbContext db,
    IFileStorageService storage,
    ILogger<ReportJobProcessor> logger)
{
    public async Task ProcessAsync(string jobId, CancellationToken ct)
    {
        var job = await db.BackgroundJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null || job.Status != "queued") return;

        job.Status   = "running";
        job.Progress = 0;
        await db.SaveChangesAsync(ct);

        try
        {
            // Derive report key from job type (e.g. "report_daily_trips" → "daily_trips")
            var key = job.Type.Replace("report_", "");

            var csv = await BuildReportCsvAsync(key, db, ct);

            await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
            var fileName  = $"reports/{key}/{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            var resultUrl = await storage.UploadAsync(stream, fileName, "text/csv", ct);

            job.Status      = "completed";
            job.Progress    = 100;
            job.ResultUrl   = resultUrl;
            job.CompletedAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Report] Job {JobId} failed", jobId);
            job.Status = "failed";
            job.Error  = ex.Message;
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task<string> BuildReportCsvAsync(string key, ApplicationDbContext db,
        CancellationToken ct)
    {
        return key switch
        {
            "daily_trips" => await BuildDailyTripsAsync(db, ct),
            "weekly_revenue" => await BuildWeeklyRevenueAsync(db, ct),
            "payout_summary" => await BuildPayoutSummaryAsync(db, ct),
            _ => $"report_key,generated_at\n{key},{DateTime.UtcNow:O}"
        };
    }

    private static async Task<string> BuildDailyTripsAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.Date;
        var rows   = await db.Trips
            .Where(t => t.CreatedAt >= cutoff)
            .Select(t => new { t.Id, t.Market, t.Vertical, t.JobState, t.FareGross, t.Currency, t.CreatedAt })
            .ToListAsync(ct);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("trip_id,market,vertical,state,fare,currency,created_at");
        foreach (var r in rows)
            sb.AppendLine($"{r.Id},{r.Market},{r.Vertical},{r.JobState},{r.FareGross},{r.Currency},{r.CreatedAt:O}");
        return sb.ToString();
    }

    private static async Task<string> BuildWeeklyRevenueAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-7);
        var rows = await db.Trips
            .Where(t => t.CreatedAt >= cutoff && t.FareIsFinal)
            .GroupBy(t => new { t.Market, t.Currency })
            .Select(g => new
            {
                g.Key.Market, g.Key.Currency,
                TripCount         = g.Count(),
                GrossRevenue      = g.Sum(t => (long)t.FareGross),
                TotalCommission   = g.Sum(t => (long)t.CommissionAmount),
                DriverEarnings    = g.Sum(t => (long)t.DriverEarnings)
            })
            .ToListAsync(ct);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("market,currency,trip_count,gross_revenue,commission,driver_earnings");
        foreach (var r in rows)
            sb.AppendLine($"{r.Market},{r.Currency},{r.TripCount},{r.GrossRevenue},{r.TotalCommission},{r.DriverEarnings}");
        return sb.ToString();
    }

    private static async Task<string> BuildPayoutSummaryAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-7);
        var rows = await db.Payouts
            .Where(p => p.CreatedAt >= cutoff)
            .Select(p => new { p.Id, p.DriverId, Amount = p.NetAmount, p.Status, p.CreatedAt })
            .ToListAsync(ct);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("payout_id,driver_id,net_amount,status,created_at");
        foreach (var r in rows)
            sb.AppendLine($"{r.Id},{r.DriverId},{r.Amount},{r.Status},{r.CreatedAt:O}");
        return sb.ToString();
    }
}
