using Hangfire;
using Izigo.Application.Common.Interfaces;
using Izigo.Infrastructure.Jobs.Processors;

namespace Izigo.Infrastructure.Jobs;

/// <summary>
/// Bridges the application layer (which knows nothing about Hangfire) to the real
/// Hangfire <see cref="IBackgroundJobClient"/>. Every command that creates a
/// BackgroundJob DB record should call one of these methods so the record actually
/// gets processed rather than sitting at "queued" forever.
/// </summary>
public class HangfireJobDispatcher(IBackgroundJobClient client) : IJobDispatcher
{
    public void Enqueue(string jobId, string jobType)
    {
        if (jobType.StartsWith("report_schedule_"))
            return; // schedule records are handled by RecurringJob, not one-shot enqueue

        if (jobType.StartsWith("report_"))
            client.Enqueue<ReportJobProcessor>(p => p.ProcessAsync(jobId, CancellationToken.None));
        else if (jobType == "broadcast")
            client.Enqueue<BroadcastJobProcessor>(p => p.ProcessAsync(jobId, CancellationToken.None));
        else if (jobType.StartsWith("dispatch:"))
            client.Enqueue<DispatchJobProcessor>(p => p.ProcessAsync(jobId, CancellationToken.None));
        else
            client.Enqueue<GenericJobProcessor>(p => p.MarkCompletedAsync(jobId, CancellationToken.None));
    }

    public void Schedule(string jobId, string jobType, DateTime utcAt)
    {
        var delay = utcAt - DateTime.UtcNow;
        if (delay <= TimeSpan.Zero) delay = TimeSpan.Zero;

        if (jobType == "broadcast")
            client.Schedule<BroadcastJobProcessor>(
                p => p.ProcessAsync(jobId, CancellationToken.None), delay);
        else
            client.Schedule<GenericJobProcessor>(
                p => p.MarkCompletedAsync(jobId, CancellationToken.None), delay);
    }
}
