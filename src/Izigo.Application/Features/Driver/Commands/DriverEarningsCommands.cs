using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Driver.Dtos;
using Izigo.Application.Features.Driver.Queries;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Driver.Commands;

// ── POST /driver/wallet/withdraw ──────────────────────────────────────────────

public record WithdrawEarningsCommand(string DriverId, WithdrawEarningsRequest Request)
    : IRequest<DriverWalletTransactionDto>;

public class WithdrawEarningsHandler(IApplicationDbContext db)
    : IRequestHandler<WithdrawEarningsCommand, DriverWalletTransactionDto>
{
    public async Task<DriverWalletTransactionDto> Handle(WithdrawEarningsCommand cmd, CancellationToken ct)
    {
        var req = cmd.Request;
        var wallet = await DriverWalletHelper.GetOrCreateAsync(cmd.DriverId, db, ct);

        if (wallet.AvailableBalance < req.Amount)
            throw new InvalidOperationException("CONFLICT: INSUFFICIENT_BALANCE");

        if (req.Amount < wallet.MinWithdrawal)
            throw new ArgumentException(
                $"VALIDATION_ERROR: Minimum withdrawal is {wallet.MinWithdrawal} {wallet.Currency}.");

        var payoutMethod = await db.PayoutMethods
            .FirstOrDefaultAsync(m => m.Id == req.PayoutMethodId && m.DriverId == cmd.DriverId, ct)
            ?? throw new KeyNotFoundException("Payout method not found.");

        var net = req.Amount - wallet.WithdrawalFee;
        wallet.AvailableBalance -= req.Amount;

        var payout = new Payout
        {
            DriverId        = cmd.DriverId,
            PayoutMethodId  = payoutMethod.Id,
            GrossAmount     = req.Amount,
            Fee             = wallet.WithdrawalFee,
            NetAmount       = net,
            Status          = PaymentStatus.Pending,
            IdempotencyKey  = req.IdempotencyKey,
            Currency        = wallet.Currency,
            Market          = "ci",
        };
        db.Payouts.Add(payout);

        var tx = new DriverWalletTransaction
        {
            DriverWalletId = wallet.Id,
            Type           = "withdrawal",
            Amount         = -req.Amount,
            BalanceAfter   = wallet.AvailableBalance,
            Status         = PaymentStatus.Processing,
            ReferenceId    = payout.Id,
        };
        db.DriverWalletTransactions.Add(tx);

        await db.SaveChangesAsync(ct);

        // TODO: trigger payout disbursement via gateway background job

        return new DriverWalletTransactionDto(tx.Id, tx.Type, tx.Amount,
            tx.BalanceAfter, "processing", tx.ReferenceId, tx.Reason, tx.CreatedAt);
    }
}

// ── POST /driver/wallet/dispute ───────────────────────────────────────────────

public record DisputeWalletCommand(string DriverId, DisputeWalletRequest Request) : IRequest;

public class DisputeWalletHandler(IApplicationDbContext db)
    : IRequestHandler<DisputeWalletCommand>
{
    public async Task Handle(DisputeWalletCommand cmd, CancellationToken ct)
    {
        var req = cmd.Request;
        db.SupportTickets.Add(new SupportTicket
        {
            UserId      = cmd.DriverId,
            UserRole    = "driver",
            Category    = "wallet_dispute",
            Description = req.Description,
            TripId      = req.TripId,
            Reference   = $"TKT-{Guid.CreateVersion7():N}"[..12],
        });
        await db.SaveChangesAsync(ct);
    }
}

// ── POST /driver/payout-methods ───────────────────────────────────────────────

public record AddPayoutMethodCommand(string DriverId, AddPayoutMethodRequest Request)
    : IRequest<PayoutMethodDto>;

public class AddPayoutMethodHandler(IApplicationDbContext db)
    : IRequestHandler<AddPayoutMethodCommand, PayoutMethodDto>
{
    public async Task<PayoutMethodDto> Handle(AddPayoutMethodCommand cmd, CancellationToken ct)
    {
        var req = cmd.Request;
        var isFirst = !await db.PayoutMethods.AnyAsync(m => m.DriverId == cmd.DriverId, ct);

        var method = new PayoutMethod
        {
            DriverId      = cmd.DriverId,
            Method        = req.Method,
            BankCode      = req.BankCode,
            AccountNumber = req.AccountNumber,
            AccountName   = req.AccountName,
            MobilePhone   = req.MobilePhone,
            IsDefault     = isFirst,
            IsVerified    = req.Method != "bank",   // mobile money verified immediately; bank needs account resolve
        };

        db.PayoutMethods.Add(method);
        await db.SaveChangesAsync(ct);

        return new PayoutMethodDto(method.Id, method.Method, method.AccountName,
            method.AccountNumber, method.BankCode, method.MobilePhone,
            method.IsDefault, method.IsVerified);
    }
}

// ── POST /driver/cash-settlement/pay ─────────────────────────────────────────

public record PayCashSettlementCommand(string DriverId, PayCashSettlementRequest Request)
    : IRequest<CashSettlementDto>;

public class PayCashSettlementHandler(IApplicationDbContext db)
    : IRequestHandler<PayCashSettlementCommand, CashSettlementDto>
{
    public async Task<CashSettlementDto> Handle(PayCashSettlementCommand cmd, CancellationToken ct)
    {
        var req = cmd.Request;
        var wallet = await DriverWalletHelper.GetOrCreateAsync(cmd.DriverId, db, ct);

        if (req.Amount > wallet.PendingCashSettlement)
            throw new ArgumentException(
                $"VALIDATION_ERROR: Amount exceeds outstanding balance of {wallet.PendingCashSettlement}.");

        wallet.PendingCashSettlement -= req.Amount;

        db.DriverWalletTransactions.Add(new DriverWalletTransaction
        {
            DriverWalletId = wallet.Id,
            Type           = "cash_settlement",
            Amount         = -req.Amount,
            BalanceAfter   = wallet.AvailableBalance,
            Status         = PaymentStatus.Succeeded,
            ReferenceId    = req.Reference,
            Reason         = "Cash settlement payment",
        });

        await db.SaveChangesAsync(ct);

        const long Cap = 10_000;
        return new CashSettlementDto(
            Outstanding: wallet.PendingCashSettlement,
            Cap:         Cap,
            IsBlocked:   wallet.PendingCashSettlement >= Cap,
            Currency:    wallet.Currency);
    }
}

// ── Internal helper ───────────────────────────────────────────────────────────

internal static class DriverWalletHelper
{
    public static async Task<Domain.Entities.DriverWallet> GetOrCreateAsync(
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
