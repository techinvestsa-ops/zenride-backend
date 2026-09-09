using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Models;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Admin.Finance.Queries;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record PaymentRowDto(string Id, string UserId, string Purpose, long Amount,
    string Currency, string Method, string Status, string? Gateway,
    string? GatewayReference, string? IdempotencyKey, string? FailureReason,
    bool IsReconciled, string Market, DateTime CreatedAt);

public record WebhookEventDto(string Id, string Gateway, string EventId, string EventType,
    string RawPayload, bool IsProcessed, DateTime? ProcessedAt, DateTime CreatedAt);

public record LedgerEntryCreatedDto(string Id, string Type, long Amount, long BalanceAfter,
    string? Reason, DateTime CreatedAt);

// Full detail: gateway ref, idempotency key, webhook_events[], ledger entries created
public record PaymentDetailDto(PaymentRowDto Payment, IEnumerable<WebhookEventDto> WebhookEvents,
    IEnumerable<LedgerEntryCreatedDto> LedgerEntries);

public record GatewayStatDto(string Gateway, int Total, int Succeeded, int Failed,
    decimal FailureRate, long Volume);

public record FailureReasonStatDto(string Reason, int Count);

public record PaymentStatsDto(IEnumerable<GatewayStatDto> ByGateway,
    IEnumerable<FailureReasonStatDto> ByReasonCode);

// ── GET /admin/payments ───────────────────────────────────────────────────────
// Filters: status, gateway, method, purpose, fail_reason, amount range, date
// meta.facets.status powers the filter chip counts

public record GetAdminPaymentsQuery(
    string Market, string? Status, string? Gateway, string? Method,
    string? Purpose, string? FailReason,
    long? MinAmount, long? MaxAmount,
    DateTime? From, DateTime? To,
    string? Q, int Page, int PerPage) : IRequest<object>;

public class GetAdminPaymentsHandler(IApplicationDbContext db)
    : IRequestHandler<GetAdminPaymentsQuery, object>
{
    public async Task<object> Handle(GetAdminPaymentsQuery req, CancellationToken ct)
    {
        var query = db.Payments.Where(p => p.Market == req.Market).AsQueryable();

        if (!string.IsNullOrWhiteSpace(req.Status) &&
            Enum.TryParse<PaymentStatus>(req.Status, true, out var status))
            query = query.Where(p => p.Status == status);

        if (!string.IsNullOrWhiteSpace(req.Gateway))
            query = query.Where(p => p.Gateway == req.Gateway);

        if (!string.IsNullOrWhiteSpace(req.Method) &&
            Enum.TryParse<PaymentMethod>(req.Method, true, out var method))
            query = query.Where(p => p.Method == method);

        if (!string.IsNullOrWhiteSpace(req.Purpose))
            query = query.Where(p => p.Purpose == req.Purpose);

        if (!string.IsNullOrWhiteSpace(req.FailReason))
            query = query.Where(p => p.FailureReason != null && p.FailureReason.Contains(req.FailReason));

        if (req.MinAmount.HasValue) query = query.Where(p => p.Amount >= req.MinAmount.Value);
        if (req.MaxAmount.HasValue) query = query.Where(p => p.Amount <= req.MaxAmount.Value);
        if (req.From.HasValue) query = query.Where(p => p.CreatedAt >= req.From.Value);
        if (req.To.HasValue)   query = query.Where(p => p.CreatedAt <= req.To.Value);

        if (!string.IsNullOrWhiteSpace(req.Q))
            query = query.Where(p => p.GatewayReference != null && p.GatewayReference.Contains(req.Q));

        // Facets
        var allStatuses = await query
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key.ToString(), Count = g.Count() })
            .ToListAsync(ct);

        var facets = new Dictionary<string, Dictionary<string, int>>
        {
            ["status"] = allStatuses.ToDictionary(g => g.Status.ToLower(), g => g.Count)
        };

        var perPage  = Math.Max(1, Math.Min(100, req.PerPage));
        var total    = await query.CountAsync(ct);
        var lastPage = (int)Math.Ceiling((double)total / perPage);

        var rows = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((req.Page - 1) * perPage)
            .Take(perPage)
            .Select(p => new PaymentRowDto(p.Id, p.UserId, p.Purpose, p.Amount, p.Currency,
                p.Method.ToString(), p.Status.ToString(), p.Gateway, p.GatewayReference,
                null, p.FailureReason, p.IsReconciled, p.Market, p.CreatedAt))
            .ToListAsync(ct);

        return AdminApiResponse.Ok(rows, new AdminPagedMeta(req.Page, perPage, total, lastPage, facets));
    }
}

// ── GET /admin/payments/{id} ──────────────────────────────────────────────────

public record GetAdminPaymentDetailQuery(string PaymentId) : IRequest<PaymentDetailDto?>;

public class GetAdminPaymentDetailHandler(IApplicationDbContext db)
    : IRequestHandler<GetAdminPaymentDetailQuery, PaymentDetailDto?>
{
    public async Task<PaymentDetailDto?> Handle(GetAdminPaymentDetailQuery req, CancellationToken ct)
    {
        var raw = await db.Payments
            .Where(p => p.Id == req.PaymentId)
            .Select(p => new { p.Id, p.UserId, p.Purpose, p.Amount, p.Currency,
                p.Method, p.Status, p.Gateway, p.GatewayReference, p.IdempotencyKey,
                p.FailureReason, p.IsReconciled, p.Market, p.CreatedAt })
            .FirstOrDefaultAsync(ct);

        if (raw is null) return null;

        var payment = new PaymentRowDto(raw.Id, raw.UserId, raw.Purpose, raw.Amount, raw.Currency,
            raw.Method.ToString(), raw.Status.ToString(), raw.Gateway, raw.GatewayReference,
            raw.IdempotencyKey, raw.FailureReason, raw.IsReconciled, raw.Market, raw.CreatedAt);

        var webhooks = await db.WebhookEvents
            .Where(w => w.PaymentId == req.PaymentId)
            .OrderByDescending(w => w.CreatedAt)
            .Select(w => new WebhookEventDto(w.Id, w.Gateway, w.EventId, w.EventType,
                w.RawPayload, w.IsProcessed, w.ProcessedAt, w.CreatedAt))
            .ToListAsync(ct);

        // Ledger entries created by this payment (wallet credits/debits referencing this payment)
        var ledgerEntries = await db.WalletTransactions
            .Where(t => t.ReferenceId == req.PaymentId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new LedgerEntryCreatedDto(t.Id, t.Type.ToString(), t.Amount,
                t.BalanceAfter, t.Reason, t.CreatedAt))
            .ToListAsync(ct);

        return new PaymentDetailDto(payment, webhooks, ledgerEntries);
    }
}

// ── GET /admin/payments/stats ─────────────────────────────────────────────────
// Volume and failure rate by gateway and by reason code

public record GetAdminPaymentStatsQuery(string Market, DateTime? From, DateTime? To)
    : IRequest<PaymentStatsDto>;

public class GetAdminPaymentStatsHandler(IApplicationDbContext db)
    : IRequestHandler<GetAdminPaymentStatsQuery, PaymentStatsDto>
{
    public async Task<PaymentStatsDto> Handle(GetAdminPaymentStatsQuery req, CancellationToken ct)
    {
        var query = db.Payments.Where(p => p.Market == req.Market).AsQueryable();
        if (req.From.HasValue) query = query.Where(p => p.CreatedAt >= req.From.Value);
        if (req.To.HasValue)   query = query.Where(p => p.CreatedAt <= req.To.Value);

        var byGatewayRaw = await query
            .GroupBy(p => p.Gateway ?? "unknown")
            .Select(g => new
            {
                Gateway   = g.Key,
                Total     = g.Count(),
                Succeeded = g.Count(p => p.Status == PaymentStatus.Succeeded),
                Failed    = g.Count(p => p.Status == PaymentStatus.Failed),
                Volume    = g.Sum(p => (long)p.Amount)
            })
            .ToListAsync(ct);

        var byGateway = byGatewayRaw.Select(g => new GatewayStatDto(
            g.Gateway, g.Total, g.Succeeded, g.Failed,
            g.Total > 0 ? Math.Round((decimal)g.Failed / g.Total * 100, 1) : 0,
            g.Volume)).ToArray();

        var byReasonRaw = await query
            .Where(p => p.Status == PaymentStatus.Failed && p.FailureReason != null)
            .GroupBy(p => p.FailureReason!)
            .OrderByDescending(g => g.Count())
            .Take(20)
            .Select(g => new FailureReasonStatDto(g.Key, g.Count()))
            .ToListAsync(ct);

        return new PaymentStatsDto(byGateway, byReasonRaw);
    }
}

// ── GET /admin/payments/export ────────────────────────────────────────────────

public record ExportPaymentsQuery(string StaffId) : IRequest<(string JobId, string StatusUrl)>;

public class ExportPaymentsHandler(IApplicationDbContext db, IJobDispatcher jobDispatcher)
    : IRequestHandler<ExportPaymentsQuery, (string JobId, string StatusUrl)>
{
    public async Task<(string JobId, string StatusUrl)> Handle(ExportPaymentsQuery req, CancellationToken ct)
    {
        var job = new Domain.Entities.BackgroundJob
            { Type = "report_payout_summary", Status = "queued", InitiatedByStaffId = req.StaffId };
        db.BackgroundJobs.Add(job);
        await db.SaveChangesAsync(ct);
        jobDispatcher.Enqueue(job.Id, job.Type);
        return (job.Id, $"/api/v1/admin/jobs/{job.Id}");
    }
}
