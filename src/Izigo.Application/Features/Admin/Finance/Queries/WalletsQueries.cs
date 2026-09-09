using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Models;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Admin.Finance.Queries;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record WalletRowDto(string Id, string AccountName, string Kind,
    long Balance, long CashOwed, long NetPosition, string Currency, bool IsLocked);

public record WalletTotalsDto(long RiderFloat, long DriverFloat,
    long CashHeldByDrivers, long NetPlatformPosition, string Currency);

public record LedgerEntryDto(string Id, string Type, long Amount, long BalanceAfter,
    string? ReferenceId, string? CreatedBy, string? Reason, DateTime CreatedAt);

public record WalletAdjustmentDto(string WalletId, string AccountName, string Kind,
    long Amount, string Direction, string? Reason, string? ActorId, DateTime CreatedAt);

// ── GET /admin/wallets ────────────────────────────────────────────────────────
// Filters: kind=rider|driver, min_balance, frozen

public record GetAdminWalletsQuery(string? Kind, long? MinBalance, bool? Frozen,
    string? Q, int Page, int PerPage) : IRequest<object>;

public class GetAdminWalletsHandler(IApplicationDbContext db)
    : IRequestHandler<GetAdminWalletsQuery, object>
{
    public async Task<object> Handle(GetAdminWalletsQuery req, CancellationToken ct)
    {
        var rows = new List<WalletRowDto>();
        var perPage  = Math.Max(1, Math.Min(100, req.PerPage));

        bool includeRider  = req.Kind is null or "rider";
        bool includeDriver = req.Kind is null or "driver";

        if (includeRider)
        {
            var riderQuery = db.Wallets.AsQueryable();
            if (req.MinBalance.HasValue) riderQuery = riderQuery.Where(w => w.Balance >= req.MinBalance.Value);
            if (req.Frozen == true)      riderQuery = riderQuery.Where(w => w.IsLocked);

            var userIds = riderQuery.Select(w => w.UserId).ToList();
            var users   = await db.Users
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FirstName, u.LastName })
                .ToDictionaryAsync(u => u.Id, ct);

            var wallets = await riderQuery
                .Select(w => new { w.Id, w.UserId, w.Balance, w.Currency, w.IsLocked })
                .ToListAsync(ct);

            if (!string.IsNullOrWhiteSpace(req.Q))
                wallets = wallets.Where(w => users.TryGetValue(w.UserId, out var u) &&
                    $"{u.FirstName} {u.LastName}".Contains(req.Q, StringComparison.OrdinalIgnoreCase)).ToList();

            rows.AddRange(wallets.Select(w =>
            {
                users.TryGetValue(w.UserId, out var u);
                return new WalletRowDto(w.Id, u is not null ? $"{u.FirstName} {u.LastName}".Trim() : "—",
                    "rider", w.Balance, 0, w.Balance, w.Currency, w.IsLocked);
            }));
        }

        if (includeDriver)
        {
            var driverQuery = db.DriverWallets.AsQueryable();
            if (req.MinBalance.HasValue) driverQuery = driverQuery.Where(w => w.AvailableBalance >= req.MinBalance.Value);
            if (req.Frozen == true)      driverQuery = driverQuery.Where(w => w.IsLocked);

            var driverIds = driverQuery.Select(w => w.DriverId).ToList();
            var drivers   = await db.DriverProfiles
                .Where(d => driverIds.Contains(d.Id))
                .Select(d => new { d.Id, d.User.FirstName, d.User.LastName })
                .ToDictionaryAsync(d => d.Id, ct);

            var dwals = await driverQuery
                .Select(w => new { w.Id, w.DriverId, w.AvailableBalance, w.PendingCashSettlement, w.Currency, w.IsLocked })
                .ToListAsync(ct);

            if (!string.IsNullOrWhiteSpace(req.Q))
                dwals = dwals.Where(w => drivers.TryGetValue(w.DriverId, out var d) &&
                    $"{d.FirstName} {d.LastName}".Contains(req.Q, StringComparison.OrdinalIgnoreCase)).ToList();

            rows.AddRange(dwals.Select(w =>
            {
                drivers.TryGetValue(w.DriverId, out var d);
                var net = w.AvailableBalance - w.PendingCashSettlement;
                return new WalletRowDto(w.Id, d is not null ? $"{d.FirstName} {d.LastName}".Trim() : "—",
                    "driver", w.AvailableBalance, w.PendingCashSettlement, net, w.Currency, w.IsLocked);
            }));
        }

        var total    = rows.Count;
        var lastPage = (int)Math.Ceiling((double)total / perPage);
        var paged    = rows.OrderByDescending(r => r.Balance)
                          .Skip((req.Page - 1) * perPage).Take(perPage).ToArray();

        return AdminApiResponse.Ok(paged, new AdminPagedMeta(req.Page, perPage, total, lastPage));
    }
}

// ── GET /admin/wallets/totals ─────────────────────────────────────────────────
// Computed from the ledger, not a cached column.

public record GetWalletTotalsQuery : IRequest<WalletTotalsDto>;

public class GetWalletTotalsHandler(IApplicationDbContext db)
    : IRequestHandler<GetWalletTotalsQuery, WalletTotalsDto>
{
    public async Task<WalletTotalsDto> Handle(GetWalletTotalsQuery req, CancellationToken ct)
    {
        // rider_float: sum of all rider wallet balances (computed from transaction ledger = same as Balance)
        var riderFloat = await db.Wallets.SumAsync(w => (long)w.Balance, ct);

        // driver_float: sum of driver available balances
        var driverFloat = await db.DriverWallets.SumAsync(w => (long)w.AvailableBalance, ct);

        // cash_held_by_drivers: cash drivers collected but haven't remitted
        var cashHeld = await db.DriverWallets.SumAsync(w => (long)w.PendingCashSettlement, ct);

        // net_platform_position: rider deposits minus cash owed by drivers
        var netPosition = riderFloat - cashHeld;

        return new WalletTotalsDto(riderFloat, driverFloat, cashHeld, netPosition, "XOF");
    }
}

// ── GET /admin/wallets/{id}/ledger ────────────────────────────────────────────
// Append-only: type, signed amount, balance_after, reference_id, created_by, reason

public record GetWalletLedgerQuery(string WalletId, int Page, int PerPage) : IRequest<object?>;

public class GetWalletLedgerHandler(IApplicationDbContext db)
    : IRequestHandler<GetWalletLedgerQuery, object?>
{
    public async Task<object?> Handle(GetWalletLedgerQuery req, CancellationToken ct)
    {
        var perPage = Math.Max(1, Math.Min(100, req.PerPage));

        // Rider wallet (wlt_ prefix)
        if (req.WalletId.StartsWith("wlt_"))
        {
            var exists = await db.Wallets.AnyAsync(w => w.Id == req.WalletId, ct);
            if (!exists) return null;

            var total = await db.WalletTransactions.CountAsync(t => t.WalletId == req.WalletId, ct);
            var items = await db.WalletTransactions
                .Where(t => t.WalletId == req.WalletId)
                .OrderByDescending(t => t.CreatedAt)
                .Skip((req.Page - 1) * perPage).Take(perPage)
                .Select(t => new LedgerEntryDto(t.Id, t.Type.ToString(), t.Amount,
                    t.BalanceAfter, t.ReferenceId, t.CreatedBy, t.Reason, t.CreatedAt))
                .ToListAsync(ct);

            return AdminApiResponse.Ok(items,
                new AdminPagedMeta(req.Page, perPage, total, (int)Math.Ceiling((double)total / perPage)));
        }

        // Driver wallet (dwl_ prefix)
        if (req.WalletId.StartsWith("dwl_"))
        {
            var exists = await db.DriverWallets.AnyAsync(w => w.Id == req.WalletId, ct);
            if (!exists) return null;

            var total = await db.DriverWalletTransactions.CountAsync(t => t.DriverWalletId == req.WalletId, ct);
            var items = await db.DriverWalletTransactions
                .Where(t => t.DriverWalletId == req.WalletId)
                .OrderByDescending(t => t.CreatedAt)
                .Skip((req.Page - 1) * perPage).Take(perPage)
                .Select(t => new LedgerEntryDto(t.Id, t.Type, t.Amount,
                    t.BalanceAfter, t.ReferenceId, t.CreatedBy, t.Reason, t.CreatedAt))
                .ToListAsync(ct);

            return AdminApiResponse.Ok(items,
                new AdminPagedMeta(req.Page, perPage, total, (int)Math.Ceiling((double)total / perPage)));
        }

        return null;
    }
}

// ── GET /admin/wallets/adjustments ────────────────────────────────────────────
// Every manual adjustment with actor and reason, filterable by actor.

public record GetWalletAdjustmentsQuery(string? Actor, int Page, int PerPage) : IRequest<object>;

public class GetWalletAdjustmentsHandler(IApplicationDbContext db)
    : IRequestHandler<GetWalletAdjustmentsQuery, object>
{
    public async Task<object> Handle(GetWalletAdjustmentsQuery req, CancellationToken ct)
    {
        var perPage = Math.Max(1, Math.Min(100, req.PerPage));

        // Rider wallet adjustments
        var riderQuery = db.WalletTransactions
            .Where(t => t.Type == WalletTransactionType.Adjustment && t.CreatedBy != null)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(req.Actor))
            riderQuery = riderQuery.Where(t => t.CreatedBy == req.Actor);

        var riderAdj = await riderQuery
            .Join(db.Wallets, t => t.WalletId, w => w.Id, (t, w) =>
                new { t, w.UserId })
            .Join(db.Users, x => x.UserId, u => u.Id, (x, u) =>
                new WalletAdjustmentDto(x.t.WalletId,
                    $"{u.FirstName} {u.LastName}", "rider",
                    x.t.Amount,
                    x.t.Amount >= 0 ? "credit" : "debit",
                    x.t.Reason, x.t.CreatedBy, x.t.CreatedAt))
            .ToListAsync(ct);

        // Driver wallet adjustments
        var driverQuery = db.DriverWalletTransactions
            .Where(t => t.Type == "adjustment" && t.CreatedBy != null)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(req.Actor))
            driverQuery = driverQuery.Where(t => t.CreatedBy == req.Actor);

        var driverAdj = await driverQuery
            .Join(db.DriverWallets, t => t.DriverWalletId, w => w.Id, (t, w) =>
                new { t, w.DriverId })
            .Join(db.DriverProfiles, x => x.DriverId, d => d.Id, (x, d) =>
                new WalletAdjustmentDto(x.t.DriverWalletId,
                    $"{d.User.FirstName} {d.User.LastName}", "driver",
                    x.t.Amount,
                    x.t.Amount >= 0 ? "credit" : "debit",
                    x.t.Reason, x.t.CreatedBy, x.t.CreatedAt))
            .ToListAsync(ct);

        var all = riderAdj.Concat(driverAdj)
            .OrderByDescending(a => a.CreatedAt).ToList();

        var total    = all.Count;
        var lastPage = (int)Math.Ceiling((double)total / perPage);
        var paged    = all.Skip((req.Page - 1) * perPage).Take(perPage).ToArray();

        return AdminApiResponse.Ok(paged, new AdminPagedMeta(req.Page, perPage, total, lastPage));
    }
}
