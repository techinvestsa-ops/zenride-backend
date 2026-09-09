using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Driver.Dtos;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Driver.Queries;

// ── GET /driver/earnings ──────────────────────────────────────────────────────

public record GetEarningsQuery(string DriverId, string Period) : IRequest<EarningsSummaryDto>;

public class GetEarningsHandler(IApplicationDbContext db)
    : IRequestHandler<GetEarningsQuery, EarningsSummaryDto>
{
    public async Task<EarningsSummaryDto> Handle(GetEarningsQuery req, CancellationToken ct)
    {
        var from = req.Period switch
        {
            "week"  => DateTime.UtcNow.Date.AddDays(-7),
            "month" => DateTime.UtcNow.Date.AddDays(-30),
            _       => DateTime.MinValue
        };

        var trips = await db.Trips
            .Where(t => t.DriverId == req.DriverId &&
                        t.JobState == JobState.Completed &&
                        (from == DateTime.MinValue || t.CompletedAt >= from))
            .ToListAsync(ct);

        var dp = await db.DriverProfiles.FirstOrDefaultAsync(d => d.UserId == req.DriverId, ct);

        return new EarningsSummaryDto(
            TotalEarnings:      trips.Sum(t => t.DriverEarnings),
            TotalTrips:         trips.Count,
            TotalOnlineMinutes: (dp?.OnlineSecondsToday ?? 0) / 60,
            CashEarnings:       trips.Where(t => t.PaymentMethod == PaymentMethod.Cash).Sum(t => t.DriverEarnings),
            WalletEarnings:     trips.Where(t => t.PaymentMethod != PaymentMethod.Cash).Sum(t => t.DriverEarnings),
            Currency:           "XOF",
            Period:             req.Period);
    }
}

// ── GET /driver/earnings/series ───────────────────────────────────────────────

public record GetEarningsSeriesQuery(string DriverId, string Granularity, int Days)
    : IRequest<EarningsSeriesDto>;

public class GetEarningsSeriesHandler(IApplicationDbContext db)
    : IRequestHandler<GetEarningsSeriesQuery, EarningsSeriesDto>
{
    public async Task<EarningsSeriesDto> Handle(GetEarningsSeriesQuery req, CancellationToken ct)
    {
        var from = DateTime.UtcNow.Date.AddDays(-req.Days);

        var trips = await db.Trips
            .Where(t => t.DriverId == req.DriverId &&
                        t.JobState == JobState.Completed &&
                        t.CompletedAt >= from)
            .Select(t => new { t.CompletedAt, t.DriverEarnings })
            .ToListAsync(ct);

        var series = trips
            .GroupBy(t => t.CompletedAt!.Value.Date.ToString("yyyy-MM-dd"))
            .OrderBy(g => g.Key)
            .Select(g => new EarningsSeriesItemDto(
                Date:     g.Key,
                Earnings: g.Sum(t => t.DriverEarnings),
                Trips:    g.Count()))
            .ToArray();

        return new EarningsSeriesDto(req.Granularity, series);
    }
}

// ── GET /driver/earnings/trips ────────────────────────────────────────────────

public record GetEarningsTripsQuery(string DriverId, int Page, int PerPage)
    : IRequest<(List<EarningsTripDto> Items, int Total)>;

public class GetEarningsTripsHandler(IApplicationDbContext db)
    : IRequestHandler<GetEarningsTripsQuery, (List<EarningsTripDto>, int)>
{
    public async Task<(List<EarningsTripDto>, int)> Handle(
        GetEarningsTripsQuery req, CancellationToken ct)
    {
        var q = db.Trips.Where(t => t.DriverId == req.DriverId &&
                                    t.JobState == JobState.Completed);

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(t => t.CompletedAt)
            .Skip((req.Page - 1) * req.PerPage)
            .Take(req.PerPage)
            .Select(t => new EarningsTripDto(
                t.Id, t.Code,
                t.Vertical.ToString().ToLower(),
                t.DriverEarnings,
                t.PaymentMethod.ToString().ToLower(),
                t.CompletedAt))
            .ToListAsync(ct);

        return (items, total);
    }
}

// ── GET /driver/wallet ────────────────────────────────────────────────────────

public record GetDriverWalletQuery(string DriverId) : IRequest<DriverWalletDto>;

public class GetDriverWalletHandler(IApplicationDbContext db)
    : IRequestHandler<GetDriverWalletQuery, DriverWalletDto>
{
    public async Task<DriverWalletDto> Handle(GetDriverWalletQuery req, CancellationToken ct)
    {
        var w = await DriverWalletQueryHelper.GetOrCreateDriverAsync(req.DriverId, db, ct);
        return ToDto(w);
    }

    internal static DriverWalletDto ToDto(Domain.Entities.DriverWallet w) =>
        new(w.Id, w.AvailableBalance, w.PendingCashSettlement, w.AdjustmentsTotal,
            w.Currency, w.InstantWithdrawalEnabled, w.MinWithdrawal, w.WithdrawalFee);
}

// ── GET /driver/wallet/transactions ───────────────────────────────────────────

public record GetDriverWalletTransactionsQuery(string DriverId, int Page, int PerPage)
    : IRequest<(List<DriverWalletTransactionDto> Items, int Total)>;

public class GetDriverWalletTransactionsHandler(IApplicationDbContext db)
    : IRequestHandler<GetDriverWalletTransactionsQuery, (List<DriverWalletTransactionDto>, int)>
{
    public async Task<(List<DriverWalletTransactionDto>, int)> Handle(
        GetDriverWalletTransactionsQuery req, CancellationToken ct)
    {
        var wallet = await DriverWalletQueryHelper.GetOrCreateDriverAsync(req.DriverId, db, ct);

        var q = db.DriverWalletTransactions.Where(t => t.DriverWalletId == wallet.Id);
        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(t => t.CreatedAt)
            .Skip((req.Page - 1) * req.PerPage)
            .Take(req.PerPage)
            .Select(t => new DriverWalletTransactionDto(
                t.Id, t.Type, t.Amount, t.BalanceAfter,
                t.Status.ToString().ToLower(), t.ReferenceId, t.Reason, t.CreatedAt))
            .ToListAsync(ct);

        return (items, total);
    }
}

// ── GET /driver/payout-methods ────────────────────────────────────────────────

public record GetPayoutMethodsQuery(string DriverId) : IRequest<List<PayoutMethodDto>>;

public class GetPayoutMethodsHandler(IApplicationDbContext db)
    : IRequestHandler<GetPayoutMethodsQuery, List<PayoutMethodDto>>
{
    public async Task<List<PayoutMethodDto>> Handle(GetPayoutMethodsQuery req, CancellationToken ct)
        => await db.PayoutMethods
            .Where(m => m.DriverId == req.DriverId)
            .OrderByDescending(m => m.IsDefault)
            .Select(m => new PayoutMethodDto(
                m.Id, m.Method, m.AccountName, m.AccountNumber,
                m.BankCode, m.MobilePhone, m.IsDefault, m.IsVerified))
            .ToListAsync(ct);
}

// ── GET /driver/cash-settlement ───────────────────────────────────────────────

public record GetCashSettlementQuery(string DriverId) : IRequest<CashSettlementDto>;

public class GetCashSettlementHandler(IApplicationDbContext db)
    : IRequestHandler<GetCashSettlementQuery, CashSettlementDto>
{
    public async Task<CashSettlementDto> Handle(GetCashSettlementQuery req, CancellationToken ct)
    {
        var wallet = await DriverWalletQueryHelper.GetOrCreateDriverAsync(req.DriverId, db, ct);
        const long Cap = 10_000;

        return new CashSettlementDto(
            Outstanding: wallet.PendingCashSettlement,
            Cap:         Cap,
            IsBlocked:   wallet.PendingCashSettlement >= Cap,
            Currency:    wallet.Currency);
    }
}

// ── Internal helper ───────────────────────────────────────────────────────────

internal static class DriverWalletQueryHelper
{
    public static async Task<Domain.Entities.DriverWallet> GetOrCreateDriverAsync(
        string driverId, IApplicationDbContext db, CancellationToken ct)
    {
        var w = await db.DriverWallets.FirstOrDefaultAsync(w => w.DriverId == driverId, ct);
        if (w != null) return w;
        w = new Domain.Entities.DriverWallet { DriverId = driverId };
        db.DriverWallets.Add(w);
        await db.SaveChangesAsync(ct);
        return w;
    }
}
