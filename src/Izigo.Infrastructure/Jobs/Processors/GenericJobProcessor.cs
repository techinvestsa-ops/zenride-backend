using Izigo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Infrastructure.Jobs.Processors;

/// <summary>
/// Fallback processor for job types that don't have a dedicated processor
/// (bulk approvals, data exports, etc.). Marks the job completed so the
/// GET /admin/jobs/{id} endpoint returns a terminal state.
/// Replace with real implementations per job type as needed.
/// </summary>
public class GenericJobProcessor(ApplicationDbContext db)
{
    public async Task MarkCompletedAsync(string jobId, CancellationToken ct)
    {
        var job = await db.BackgroundJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null || job.Status != "queued") return;

        job.Status      = "completed";
        job.Progress    = 100;
        job.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
