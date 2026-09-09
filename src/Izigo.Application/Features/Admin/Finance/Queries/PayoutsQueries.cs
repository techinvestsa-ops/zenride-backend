using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Models;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Admin.Finance.Queries;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record PayoutAttemptDto(string Id, string Status, string? ProviderReference,
    string? FailureReason, DateTime CreatedAt);

public record PayoutRowDto(string Id, string DriverId, string DriverName,
    string DestinationMasked, long GrossAmount, long Fee, long NetAmount, string Currency,
    string Status, string? FailureReason, long DriverCashOwed, DateTime CreatedAt);

public record PayoutDetailDto(PayoutRowDto Payout, IEnumerable<PayoutAttemptDto> AttemptHistory,
    string? NameEnquiryResult);

public record CashSettlementRowDto(string DriverId, string DriverName, long CashOwed,
    string Currency, int DaysOutstanding, bool OverCap);

// ── GET /admin/payouts ────────────────────────────────────────────────────────
// Fields include driver_cash_owed so reviewer sees the offset without opening the row.

public record GetAdminPayoutsQuery(string Market, string? Status, string? Bank,
    long? MinAmount, long? MaxAmount, DateTime? From, DateTime? To, int Page, int PerPage)
    : IRequest<object>;

public class GetAdminPayoutsHandler(IApplicationDbContext db)
    : IRequestHandler<GetAdminPayoutsQuery, object>
{
    public async Task<object> Handle(GetAdminPayoutsQuery req, CancellationToken ct)
    {
        var query = db.Payouts.Where(p => p.Market == req.Market).AsQueryable();

        if (!string.IsNullOrWhiteSpace(req.Status) &&
            Enum.TryParse<PaymentStatus>(req.Status, true, out var status))
            query = query.Where(p => p.Status == status);

        // bank filter: match against the driver's payout method BankCode or Provider
        if (!string.IsNullOrWhiteSpace(req.Bank))
        {
            var driverIdsForBank = await db.PayoutMethods
                .Where(m => m.BankCode == req.Bank || m.Provider == req.Bank)
                .Select(m => m.DriverId).Distinct().ToListAsync(ct);
            query = query.Where(p => driverIdsForBank.Contains(p.DriverId));
        }

        if (req.MinAmount.HasValue) query = query.Where(p => p.GrossAmount >= req.MinAmount.Value);
        if (req.MaxAmount.HasValue) query = query.Where(p => p.GrossAmount <= req.MaxAmount.Value);
        if (req.From.HasValue) query = query.Where(p => p.CreatedAt >= req.From.Value);
        if (req.To.HasValue)   query = query.Where(p => p.CreatedAt <= req.To.Value);

        var facets = new Dictionary<string, Dictionary<string, int>>
        {
            ["status"] = await query.GroupBy(p => p.Status)
                .Select(g => new { Status = g.Key.ToString(), Count = g.Count() })
                .ToDictionaryAsync(g => g.Status.ToLower(), g => g.Count, ct)
        };

        var perPage  = Math.Max(1, Math.Min(100, req.PerPage));
        var total    = await query.CountAsync(ct);
        var lastPage = (int)Math.Ceiling((double)total / perPage);

        var payouts = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((req.Page - 1) * perPage).Take(perPage)
            .Select(p => new { p.Id, p.DriverId, p.PayoutMethodId, p.GrossAmount, p.Fee,
                               p.NetAmount, p.Currency, p.Status, p.FailureReason, p.CreatedAt })
            .ToListAsync(ct);

        var driverIds = payouts.Select(p => p.DriverId).Distinct().ToList();
        var drivers   = await db.DriverProfiles
            .Where(d => driverIds.Contains(d.Id))
            .Select(d => new { d.Id, d.User.FirstName, d.User.LastName })
            .ToDictionaryAsync(d => d.Id, ct);

        var methodIds = payouts.Select(p => p.PayoutMethodId).Distinct().ToList();
        var methods   = await db.PayoutMethods
            .Where(m => methodIds.Contains(m.Id))
            .Select(m => new { m.Id, m.AccountNumber, m.MobilePhone })
            .ToDictionaryAsync(m => m.Id, ct);

        var cashOwed = await db.DriverWallets
            .Where(w => driverIds.Contains(w.DriverId))
            .Select(w => new { w.DriverId, w.PendingCashSettlement })
            .ToDictionaryAsync(w => w.DriverId, w => w.PendingCashSettlement, ct);

        var rows = payouts.Select(p =>
        {
            drivers.TryGetValue(p.DriverId, out var drv);
            methods.TryGetValue(p.PayoutMethodId, out var method);
            cashOwed.TryGetValue(p.DriverId, out var owed);

            // Mask destination: show last 4 only
            var dest = method?.AccountNumber is not null
                ? $"•••• {method.AccountNumber[^Math.Min(4, method.AccountNumber.Length)..]}"
                : method?.MobilePhone is not null
                    ? Ops.Queries.GetLiveOpsHandler.MaskPhone(method.MobilePhone)
                    : "—";

            return new PayoutRowDto(p.Id, p.DriverId,
                drv is not null ? $"{drv.FirstName} {drv.LastName}".Trim() : "—",
                dest, p.GrossAmount, p.Fee, p.NetAmount, p.Currency,
                p.Status.ToString(), p.FailureReason, owed, p.CreatedAt);
        }).ToArray();

        return AdminApiResponse.Ok(rows, new AdminPagedMeta(req.Page, perPage, total, lastPage, facets));
    }
}

// ── GET /admin/payouts/{id} ───────────────────────────────────────────────────

public record GetAdminPayoutDetailQuery(string PayoutId) : IRequest<PayoutDetailDto?>;

public class GetAdminPayoutDetailHandler(IApplicationDbContext db)
    : IRequestHandler<GetAdminPayoutDetailQuery, PayoutDetailDto?>
{
    public async Task<PayoutDetailDto?> Handle(GetAdminPayoutDetailQuery req, CancellationToken ct)
    {
        var payout = await db.Payouts
            .Where(p => p.Id == req.PayoutId)
            .Select(p => new { p.Id, p.DriverId, p.PayoutMethodId, p.GrossAmount, p.Fee,
                               p.NetAmount, p.Currency, p.Status, p.FailureReason, p.CreatedAt })
            .FirstOrDefaultAsync(ct);

        if (payout is null) return null;

        var drv = await db.DriverProfiles
            .Where(d => d.Id == payout.DriverId)
            .Select(d => new { d.User.FirstName, d.User.LastName })
            .FirstOrDefaultAsync(ct);

        var method = await db.PayoutMethods
            .Where(m => m.Id == payout.PayoutMethodId)
            .Select(m => new { m.AccountNumber, m.MobilePhone, m.AccountName })
            .FirstOrDefaultAsync(ct);

        var dest = method?.AccountNumber is not null
            ? $"•••• {method.AccountNumber[^Math.Min(4, method.AccountNumber.Length)..]}"
            : method?.MobilePhone is not null
                ? Ops.Queries.GetLiveOpsHandler.MaskPhone(method.MobilePhone)
                : "—";

        var row = new PayoutRowDto(payout.Id, payout.DriverId,
            drv is not null ? $"{drv.FirstName} {drv.LastName}".Trim() : "—",
            dest, payout.GrossAmount, payout.Fee, payout.NetAmount, payout.Currency,
            payout.Status.ToString(), payout.FailureReason, 0, payout.CreatedAt);

        var attempts = await db.PayoutAttempts
            .Where(a => a.PayoutId == req.PayoutId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new PayoutAttemptDto(a.Id, a.Status.ToString(),
                a.ProviderReference, a.FailureReason, a.CreatedAt))
            .ToListAsync(ct);

        return new PayoutDetailDto(row, attempts, method?.AccountName);
    }
}

// ── GET /admin/cash-settlement ────────────────────────────────────────────────
// Ranked by amount, with days outstanding and whether over the cap.

public record GetCashSettlementQuery(string Market) : IRequest<object>;

public class GetCashSettlementHandler(IApplicationDbContext db)
    : IRequestHandler<GetCashSettlementQuery, object>
{
    public async Task<object> Handle(GetCashSettlementQuery req, CancellationToken ct)
    {
        var cap = await db.CommissionConfigs
            .Where(c => c.Market == req.Market)
            .Select(c => (long?)c.CashSettlementCap)
            .FirstOrDefaultAsync(ct) ?? 10_000L;

        var wallets = await db.DriverWallets
            .Where(w => w.PendingCashSettlement > 0)
            .Select(w => new { w.DriverId, w.PendingCashSettlement, w.Currency })
            .OrderByDescending(w => w.PendingCashSettlement)
            .ToListAsync(ct);

        var driverIds = wallets.Select(w => w.DriverId).ToList();
        var drivers   = await db.DriverProfiles
            .Where(d => driverIds.Contains(d.Id))
            .Select(d => new { d.Id, d.User.FirstName, d.User.LastName })
            .ToDictionaryAsync(d => d.Id, ct);

        // Days outstanding: from the oldest unsettled earning transaction
        var oldestTxDates = await db.DriverWalletTransactions
            .Where(t => driverIds.Contains(
                db.DriverWallets.Where(w => w.Id == t.DriverWalletId)
                    .Select(w => w.DriverId).FirstOrDefault()!)
                 && t.Type == "trip_earning")
            .GroupBy(t => db.DriverWallets.Where(w => w.Id == t.DriverWalletId)
                .Select(w => w.DriverId).First())
            .Select(g => new { DriverId = g.Key, OldestDate = g.Min(t => t.CreatedAt) })
            .ToDictionaryAsync(g => g.DriverId, g => g.OldestDate, ct);

        var now = DateTime.UtcNow;
        var rows = wallets.Select(w =>
        {
            drivers.TryGetValue(w.DriverId, out var drv);
            oldestTxDates.TryGetValue(w.DriverId, out var oldest);
            var days = oldest == default ? 0 : (int)(now - oldest).TotalDays;

            return new CashSettlementRowDto(w.DriverId,
                drv is not null ? $"{drv.FirstName} {drv.LastName}".Trim() : "—",
                w.PendingCashSettlement, w.Currency,
                days, w.PendingCashSettlement > cap);
        }).ToArray();

        return AdminApiResponse.Ok(rows, new AdminPagedMeta(1, rows.Length, rows.Length, 1));
    }
}
