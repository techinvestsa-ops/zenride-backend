using Izigo.Application.Common.Interfaces;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Admin.Finance.Commands;

// ── POST /admin/reconciliation/import-statement ───────────────────────────────
// multipart CSV → matches against ledger, returns unmatched rows both ways.
// Returns 202 + job_id for processing.

public record ImportStatementCommand(string StaffId, string Market, byte[]? CsvContent)
    : IRequest<(string JobId, string StatusUrl)>;

public class ImportStatementHandler(IApplicationDbContext db, IJobDispatcher jobDispatcher)
    : IRequestHandler<ImportStatementCommand, (string JobId, string StatusUrl)>
{
    public async Task<(string JobId, string StatusUrl)> Handle(ImportStatementCommand cmd, CancellationToken ct)
    {
        var job = new Domain.Entities.BackgroundJob
        {
            Type               = "reconciliation_import",
            Status             = "queued",
            InitiatedByStaffId = cmd.StaffId
        };
        db.BackgroundJobs.Add(job);
        await db.SaveChangesAsync(ct);
        jobDispatcher.Enqueue(job.Id, job.Type);
        return (job.Id, $"/api/v1/admin/jobs/{job.Id}");
    }
}

// ── POST /admin/reconciliation/replay-missed ──────────────────────────────────
// Batch version of per-payment webhook replay.

public record ReplayMissedCommand(string Market, string? Gateway, string StaffId, string StaffName)
    : IRequest<(string JobId, string StatusUrl)>;

public class ReplayMissedHandler(IApplicationDbContext db, IAuditService audit,
    IJobDispatcher jobDispatcher)
    : IRequestHandler<ReplayMissedCommand, (string JobId, string StatusUrl)>
{
    public async Task<(string JobId, string StatusUrl)> Handle(ReplayMissedCommand cmd, CancellationToken ct)
    {
        var query = db.WebhookEvents.Where(w => !w.IsProcessed).AsQueryable();
        if (!string.IsNullOrWhiteSpace(cmd.Gateway))
            query = query.Where(w => w.Gateway == cmd.Gateway);

        var count = await query.CountAsync(ct);

        var job = new Domain.Entities.BackgroundJob
        {
            Type               = "replay_webhooks",
            Status             = "queued",
            InitiatedByStaffId = cmd.StaffId
        };
        db.BackgroundJobs.Add(job);

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.PaymentReplayWebhook,
            "Reconciliation", cmd.Market, reason: $"Batch replay {count} missed webhooks",
            after: new { Count = count, cmd.Gateway }, market: cmd.Market, ct: ct);

        await db.SaveChangesAsync(ct);
        jobDispatcher.Enqueue(job.Id, job.Type);
        return (job.Id, $"/api/v1/admin/jobs/{job.Id}");
    }
}
