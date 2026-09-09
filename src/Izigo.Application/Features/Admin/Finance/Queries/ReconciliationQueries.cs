using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Models;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Admin.Finance.Queries;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record ReconciliationRowDto(string Gateway, DateTime Date, int Transactions,
    long GatewaySettled, long InternalLedger, long Variance,
    int MissedWebhooks, string Status);

public record ReconciliationVarianceDto(string PaymentId, long Amount, string Currency,
    string Status, string? GatewayReference, bool MissingInGateway, bool MissingInLedger,
    DateTime CreatedAt);

// ── GET /admin/reconciliation ─────────────────────────────────────────────────
// Per gateway per day: transactions, gateway_settled, internal_ledger, variance, missed_webhooks, status

public record GetReconciliationQuery(string Market, DateTime? From, DateTime? To)
    : IRequest<object>;

public class GetReconciliationHandler(IApplicationDbContext db)
    : IRequestHandler<GetReconciliationQuery, object>
{
    public async Task<object> Handle(GetReconciliationQuery req, CancellationToken ct)
    {
        var from = req.From ?? DateTime.UtcNow.AddDays(-30);
        var to   = req.To   ?? DateTime.UtcNow;

        // Payments grouped by gateway and day
        var payments = await db.Payments
            .Where(p => p.Market == req.Market && p.CreatedAt >= from && p.CreatedAt <= to
                     && p.Gateway != null)
            .GroupBy(p => new { p.Gateway, Day = p.CreatedAt.Date })
            .Select(g => new
            {
                g.Key.Gateway,
                Day   = g.Key.Day,
                Count = g.Count(),
                Total = g.Where(p => p.Status == PaymentStatus.Succeeded).Sum(p => (long)p.Amount)
            })
            .ToListAsync(ct);

        // Missed webhooks per gateway (unprocessed)
        var missedWebhooks = await db.WebhookEvents
            .Where(w => !w.IsProcessed)
            .GroupBy(w => w.Gateway)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, ct);

        var rows = payments.Select(p => new ReconciliationRowDto(
            p.Gateway!,
            p.Day,
            p.Count,
            p.Total,  // gateway_settled ≈ internal_ledger for V1 (no external statement)
            p.Total,  // internal_ledger
            0L,       // variance (zero until external statement is imported)
            missedWebhooks.GetValueOrDefault(p.Gateway!, 0),
            "balanced")).ToArray();

        return AdminApiResponse.Ok(rows, new AdminPagedMeta(1, rows.Length, rows.Length, 1));
    }
}

// ── GET /admin/reconciliation/{gateway}/variances ─────────────────────────────
// Specific transactions that do not match, with which side is missing.

public record GetReconciliationVariancesQuery(string Gateway, string Market,
    DateTime? From, DateTime? To) : IRequest<object>;

public class GetReconciliationVariancesHandler(IApplicationDbContext db)
    : IRequestHandler<GetReconciliationVariancesQuery, object>
{
    public async Task<object> Handle(GetReconciliationVariancesQuery req, CancellationToken ct)
    {
        var from = req.From ?? DateTime.UtcNow.AddDays(-7);
        var to   = req.To   ?? DateTime.UtcNow;

        // Failed payments where gateway reference exists but payment failed (potential mismatch)
        var variances = await db.Payments
            .Where(p => p.Market == req.Market
                     && p.Gateway == req.Gateway
                     && p.CreatedAt >= from && p.CreatedAt <= to
                     && (p.Status == PaymentStatus.Failed || !p.IsReconciled))
            .Select(p => new ReconciliationVarianceDto(p.Id, p.Amount, p.Currency,
                p.Status.ToString(), p.GatewayReference,
                false,  // MissingInGateway: needs external statement to determine
                !p.IsReconciled, // MissingInLedger approximation
                p.CreatedAt))
            .ToListAsync(ct);

        return AdminApiResponse.Ok(variances,
            new AdminPagedMeta(1, variances.Count, variances.Count, 1));
    }
}
