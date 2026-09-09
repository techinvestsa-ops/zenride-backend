using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Wallets.Dtos;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Wallets.Queries;

// ── GET /wallet ───────────────────────────────────────────────────────────────

public record GetWalletQuery(string UserId) : IRequest<WalletDto>;

public class GetWalletHandler(IApplicationDbContext db)
    : IRequestHandler<GetWalletQuery, WalletDto>
{
    public async Task<WalletDto> Handle(GetWalletQuery req, CancellationToken ct)
    {
        var wallet = await WalletHelper.GetOrCreateAsync(req.UserId, db, ct);
        return ToDto(wallet);
    }

    internal static WalletDto ToDto(Domain.Entities.Wallet w) =>
        new(w.Id, w.Balance, w.PendingIn, w.PendingOut, w.Currency,
            w.IsLocked, w.LockReason, w.MinTopup, w.MaxBalance);
}

// ── GET /wallet/transactions ──────────────────────────────────────────────────

public record GetWalletTransactionsQuery(
    string UserId, string? Type, DateTime? From, DateTime? To, int Page, int PerPage)
    : IRequest<(List<WalletTransactionDto> Items, int Total)>;

public class GetWalletTransactionsHandler(IApplicationDbContext db)
    : IRequestHandler<GetWalletTransactionsQuery, (List<WalletTransactionDto>, int)>
{
    public async Task<(List<WalletTransactionDto>, int)> Handle(
        GetWalletTransactionsQuery req, CancellationToken ct)
    {
        var wallet = await WalletHelper.GetOrCreateAsync(req.UserId, db, ct);

        var q = db.WalletTransactions.Where(t => t.WalletId == wallet.Id);

        if (!string.IsNullOrEmpty(req.Type) &&
            Enum.TryParse<WalletTransactionType>(req.Type, true, out var txType))
            q = q.Where(t => t.Type == txType);

        if (req.From.HasValue) q = q.Where(t => t.CreatedAt >= req.From.Value);
        if (req.To.HasValue)   q = q.Where(t => t.CreatedAt <= req.To.Value);

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(t => t.CreatedAt)
            .Skip((req.Page - 1) * req.PerPage)
            .Take(req.PerPage)
            .Select(t => new WalletTransactionDto(
                t.Id, t.Type.ToString().ToLower(), t.Amount, t.BalanceAfter,
                t.Title, t.Subtitle, t.Status.ToString().ToLower(),
                t.ReferenceId, t.CreatedAt))
            .ToListAsync(ct);

        return (items, total);
    }
}

// ── GET /wallet/summary ───────────────────────────────────────────────────────

public record GetWalletSummaryQuery(string UserId, string Period) : IRequest<WalletSummaryDto>;

public class GetWalletSummaryHandler(IApplicationDbContext db)
    : IRequestHandler<GetWalletSummaryQuery, WalletSummaryDto>
{
    public async Task<WalletSummaryDto> Handle(GetWalletSummaryQuery req, CancellationToken ct)
    {
        var wallet = await WalletHelper.GetOrCreateAsync(req.UserId, db, ct);
        var now    = DateTime.UtcNow;

        var from = req.Period == "month"
            ? now.AddDays(-29).Date
            : now.AddDays(-6).Date;   // 7-day window for "week"

        var txns = await db.WalletTransactions
            .Where(t => t.WalletId == wallet.Id
                     && t.Status == PaymentStatus.Succeeded
                     && t.Amount < 0                           // spending only
                     && t.CreatedAt >= from)
            .Select(t => new { t.CreatedAt, t.Amount })
            .ToListAsync(ct);

        WalletBucketDto[] buckets;

        if (req.Period == "month")
        {
            // 5-week buckets
            buckets = Enumerable.Range(0, 5).Select(i =>
            {
                var wStart = from.AddDays(i * 7);
                var wEnd   = wStart.AddDays(7);
                var label  = wStart.ToString("dd MMM");
                var total  = txns.Where(t => t.CreatedAt.Date >= wStart && t.CreatedAt.Date < wEnd)
                                  .Sum(t => Math.Abs(t.Amount));
                return new WalletBucketDto(label, total);
            }).ToArray();
        }
        else
        {
            // 7-day buckets labelled Mon–Sun
            buckets = Enumerable.Range(0, 7).Select(i =>
            {
                var day   = from.AddDays(i);
                var label = day.ToString("ddd");
                var total = txns.Where(t => t.CreatedAt.Date == day).Sum(t => Math.Abs(t.Amount));
                return new WalletBucketDto(label, total);
            }).ToArray();
        }

        return new WalletSummaryDto(req.Period, wallet.Currency, buckets);
    }
}

// ── GET /wallet/limits ────────────────────────────────────────────────────────

public record GetWalletLimitsQuery(string UserId) : IRequest<WalletLimitsDto>;

public class GetWalletLimitsHandler(IApplicationDbContext db)
    : IRequestHandler<GetWalletLimitsQuery, WalletLimitsDto>
{
    public async Task<WalletLimitsDto> Handle(GetWalletLimitsQuery req, CancellationToken ct)
    {
        var wallet = await WalletHelper.GetOrCreateAsync(req.UserId, db, ct);
        return new WalletLimitsDto(
            MinTopup:      wallet.MinTopup,
            MaxTopup:      500_000,
            MaxBalance:    wallet.MaxBalance,
            MaxTransfer:   200_000,
            MaxWithdrawal: 500_000,
            Currency:      wallet.Currency);
    }
}

// ── POST /wallet/transfer/lookup ──────────────────────────────────────────────

public record LookupTransferRecipientQuery(string UserId, TransferLookupRequest Request)
    : IRequest<WalletRecipientDto>;

public class LookupTransferRecipientHandler(IApplicationDbContext db)
    : IRequestHandler<LookupTransferRecipientQuery, WalletRecipientDto>
{
    public async Task<WalletRecipientDto> Handle(LookupTransferRecipientQuery req, CancellationToken ct)
    {
        var recipient = await db.Users
            .FirstOrDefaultAsync(u => u.Phone == req.Request.Phone, ct)
            ?? throw new KeyNotFoundException("No user found with that phone number.");

        if (recipient.Id == req.UserId)
            throw new ArgumentException("VALIDATION_ERROR: Cannot transfer to yourself.");

        return new WalletRecipientDto(recipient.Id, recipient.FullName, recipient.Phone, recipient.PhotoUrl);
    }
}
