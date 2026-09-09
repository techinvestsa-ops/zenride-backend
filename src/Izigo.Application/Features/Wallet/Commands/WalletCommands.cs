using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Payments.Commands;
using Izigo.Application.Features.Payments.Dtos;
using Izigo.Application.Features.Realtime.Dtos;
using Izigo.Application.Features.Wallets.Dtos;
using Izigo.Application.Features.Wallets.Queries;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Wallets.Commands;

// ── POST /wallet/topup ────────────────────────────────────────────────────────

public record TopUpCommand(string UserId, TopUpRequest Request) : IRequest<PaymentIntentDto>;

public class TopUpHandler(IApplicationDbContext db, IMediator mediator)
    : IRequestHandler<TopUpCommand, PaymentIntentDto>
{
    public async Task<PaymentIntentDto> Handle(TopUpCommand cmd, CancellationToken ct)
    {
        var req = cmd.Request;
        var wallet = await WalletHelper.GetOrCreateAsync(cmd.UserId, db, ct);

        if (wallet.IsLocked)
            throw new InvalidOperationException("CONFLICT: Wallet is locked. Contact support.");

        if (req.Amount < wallet.MinTopup)
            throw new ArgumentException($"VALIDATION_ERROR: Minimum top-up is {wallet.MinTopup} {wallet.Currency}.");

        if (wallet.Balance + req.Amount > wallet.MaxBalance)
            throw new InvalidOperationException(
                $"CONFLICT: Top-up would exceed maximum wallet balance of {wallet.MaxBalance}.");

        return await mediator.Send(new CreatePaymentIntentCommand(cmd.UserId,
            new CreatePaymentIntentRequest(
                Purpose: "topup",
                ReferenceId: wallet.Id,
                Amount: req.Amount,
                Currency: wallet.Currency,
                Method: req.Method,
                Phone: req.Phone,
                IdempotencyKey: req.IdempotencyKey)), ct);
    }
}

// ── POST /wallet/transfer ─────────────────────────────────────────────────────

public record TransferCommand(string SenderId, TransferRequest Request) : IRequest<WalletTransactionDto>;

public class TransferHandler(IApplicationDbContext db, IRealtimeService realtime)
    : IRequestHandler<TransferCommand, WalletTransactionDto>
{
    public async Task<WalletTransactionDto> Handle(TransferCommand cmd, CancellationToken ct)
    {
        var req = cmd.Request;

        // Idempotency
        if (req.IdempotencyKey != null)
        {
            var existing = await db.WalletTransactions
                .FirstOrDefaultAsync(t => t.ReferenceId == $"transfer:{req.IdempotencyKey}", ct);
            if (existing != null)
                return new WalletTransactionDto(existing.Id, "transfer_out",
                    existing.Amount, existing.BalanceAfter, existing.Title,
                    existing.Subtitle, existing.Status.ToString().ToLower(),
                    existing.ReferenceId, existing.CreatedAt);
        }

        var senderWallet = await WalletHelper.GetOrCreateAsync(cmd.SenderId, db, ct);
        var recipientWallet = await WalletHelper.GetOrCreateAsync(req.RecipientUserId, db, ct);

        if (senderWallet.IsLocked)
            throw new InvalidOperationException("CONFLICT: Your wallet is locked.");

        if (senderWallet.Balance < req.Amount)
            throw new InvalidOperationException("CONFLICT: INSUFFICIENT_BALANCE");

        if (req.Amount > 200_000)
            throw new ArgumentException("VALIDATION_ERROR: Transfer exceeds maximum limit of 200,000.");

        senderWallet.Balance -= req.Amount;
        recipientWallet.Balance += req.Amount;

        var refId = $"transfer:{req.IdempotencyKey ?? Guid.CreateVersion7().ToString("N")}";

        var recipientUser = await db.Users.FirstOrDefaultAsync(u => u.Id == req.RecipientUserId, ct);
        var recipientName = recipientUser?.FullName ?? "User";

        var outTx = new WalletTransaction
        {
            WalletId     = senderWallet.Id,
            Type         = WalletTransactionType.TransferOut,
            Amount       = -req.Amount,
            BalanceAfter = senderWallet.Balance,
            Title        = $"Transfer to {recipientName}",
            Subtitle     = req.Note,
            ReferenceId  = refId,
            Status       = PaymentStatus.Succeeded,
        };

        db.WalletTransactions.Add(outTx);
        db.WalletTransactions.Add(new WalletTransaction
        {
            WalletId     = recipientWallet.Id,
            Type         = WalletTransactionType.TransferIn,
            Amount       = req.Amount,
            BalanceAfter = recipientWallet.Balance,
            Title        = "Transfer received",
            Subtitle     = req.Note,
            ReferenceId  = refId,
            Status       = PaymentStatus.Succeeded,
        });

        await db.SaveChangesAsync(ct);

        // wallet.balance_changed → both parties' private channels
        var senderEvent = new WalletBalanceChangedEvent(senderWallet.Balance, -req.Amount, outTx.Id);
        await realtime.PublishToUserAsync(cmd.SenderId, "wallet.balance_changed", senderEvent, ct);

        return new WalletTransactionDto(outTx.Id, "transfer_out",
            outTx.Amount, outTx.BalanceAfter, outTx.Title,
            outTx.Subtitle, "succeeded", outTx.ReferenceId, outTx.CreatedAt);
    }
}

// ── POST /wallet/withdraw ─────────────────────────────────────────────────────

public record WithdrawCommand(string UserId, WithdrawRequest Request) : IRequest<WalletTransactionDto>;

public class WithdrawHandler(IApplicationDbContext db, IRealtimeService realtime)
    : IRequestHandler<WithdrawCommand, WalletTransactionDto>
{
    public async Task<WalletTransactionDto> Handle(WithdrawCommand cmd, CancellationToken ct)
    {
        var req = cmd.Request;
        var wallet = await WalletHelper.GetOrCreateAsync(cmd.UserId, db, ct);

        if (wallet.IsLocked)
            throw new InvalidOperationException("CONFLICT: Wallet is locked.");

        if (wallet.Balance < req.Amount)
            throw new InvalidOperationException("CONFLICT: INSUFFICIENT_BALANCE");

        var payMethod = await db.UserPaymentMethods
            .FirstOrDefaultAsync(m => m.Id == req.PaymentMethodId && m.UserId == cmd.UserId, ct)
            ?? throw new KeyNotFoundException("Payment method not found.");

        wallet.Balance      -= req.Amount;
        wallet.PendingOut   += req.Amount;

        var tx = new WalletTransaction
        {
            WalletId     = wallet.Id,
            Type         = WalletTransactionType.Withdrawal,
            Amount       = -req.Amount,
            BalanceAfter = wallet.Balance,
            Title        = $"Withdrawal to {payMethod.Label}",
            ReferenceId  = req.IdempotencyKey,
            Status       = PaymentStatus.Processing,   // confirmed when payout completes
        };

        db.WalletTransactions.Add(tx);
        await db.SaveChangesAsync(ct);

        // wallet.balance_changed → rider's private channel
        await realtime.PublishToUserAsync(cmd.UserId, "wallet.balance_changed",
            new WalletBalanceChangedEvent(wallet.Balance, -req.Amount, tx.Id), ct);

        // TODO: trigger payout via gateway in background job

        return new WalletTransactionDto(tx.Id, "withdrawal",
            tx.Amount, tx.BalanceAfter, tx.Title,
            tx.Subtitle, "processing", tx.ReferenceId, tx.CreatedAt);
    }
}
