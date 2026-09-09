using Izigo.Application.Common.Interfaces;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Admin.Finance.Commands;

public record FinanceCommandResult(bool Success, string? ErrorCode, object? Data = null);

// ── POST /admin/payments/{id}/retry ──────────────────────────────────────────
// New idempotency key, links back to the original attempt.

public record RetryPaymentCommand(string PaymentId, string StaffId, string StaffName)
    : IRequest<FinanceCommandResult>;

public class RetryPaymentHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<RetryPaymentCommand, FinanceCommandResult>
{
    public async Task<FinanceCommandResult> Handle(RetryPaymentCommand cmd, CancellationToken ct)
    {
        var original = await db.Payments.FirstOrDefaultAsync(p => p.Id == cmd.PaymentId, ct);
        if (original is null) return new(false, "PAYMENT_NOT_FOUND");
        if (original.Status != PaymentStatus.Failed) return new(false, "PAYMENT_NOT_FAILED");

        // Create a new payment linked to the original
        var retry = new Domain.Entities.Payment
        {
            UserId      = original.UserId,
            Purpose     = original.Purpose,
            ReferenceId = original.ReferenceId,
            Amount      = original.Amount,
            Currency    = original.Currency,
            Method      = original.Method,
            Gateway     = original.Gateway,
            Market      = original.Market,
            IdempotencyKey = $"retry_{cmd.PaymentId}_{DateTime.UtcNow.Ticks}"
        };
        db.Payments.Add(retry);

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.PaymentRetry,
            "Payment", cmd.PaymentId, reason: "Admin initiated retry",
            after: new { RetryPaymentId = retry.Id }, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null, new { retry_payment_id = retry.Id });
    }
}

// ── POST /admin/payments/{id}/refund ─────────────────────────────────────────
// amount?, reason. Distinct from trip refund — this is for topups and duplicates.

public record RefundPaymentCommand(string PaymentId, long? Amount, string Reason,
    string StaffId, string StaffName) : IRequest<FinanceCommandResult>;

public class RefundPaymentHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<RefundPaymentCommand, FinanceCommandResult>
{
    public async Task<FinanceCommandResult> Handle(RefundPaymentCommand cmd, CancellationToken ct)
    {
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == cmd.PaymentId, ct);
        if (payment is null) return new(false, "PAYMENT_NOT_FOUND");
        if (payment.Status != PaymentStatus.Succeeded) return new(false, "PAYMENT_NOT_SUCCEEDED");

        var refundAmount = cmd.Amount ?? payment.Amount;
        if (refundAmount <= 0 || refundAmount > payment.Amount)
            return new(false, "INVALID_REFUND_AMOUNT");

        // For wallet-based payments: credit the user's wallet
        if (payment.Method == PaymentMethod.Wallet)
        {
            var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == payment.UserId, ct);
            if (wallet is not null)
            {
                wallet.Balance += refundAmount;
                db.WalletTransactions.Add(new Domain.Entities.WalletTransaction
                {
                    WalletId    = wallet.Id,
                    Type        = WalletTransactionType.Refund,
                    Amount      = refundAmount,
                    BalanceAfter = wallet.Balance,
                    Title       = "Payment refund",
                    Subtitle    = cmd.Reason,
                    ReferenceId = payment.Id,
                    Reason      = cmd.Reason,
                    CreatedBy   = cmd.StaffId
                });
            }
        }
        // For gateway payments: gateway refund would be triggered here

        payment.Status = PaymentStatus.Refunded;

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.PaymentRefund,
            "Payment", cmd.PaymentId, cmd.Reason,
            after: new { RefundAmount = refundAmount }, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── POST /admin/payments/{id}/replay-webhook ──────────────────────────────────
// Idempotent by gateway event id. Fix for "gateway says paid, we say pending".

public record ReplayWebhookCommand(string PaymentId, string? EventId,
    string StaffId, string StaffName) : IRequest<FinanceCommandResult>;

public class ReplayWebhookHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<ReplayWebhookCommand, FinanceCommandResult>
{
    public async Task<FinanceCommandResult> Handle(ReplayWebhookCommand cmd, CancellationToken ct)
    {
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == cmd.PaymentId, ct);
        if (payment is null) return new(false, "PAYMENT_NOT_FOUND");

        // Idempotent by gateway event id: if the specific event was already processed, skip
        var latestWebhook = await db.WebhookEvents
            .Where(w => w.PaymentId == cmd.PaymentId &&
                       (cmd.EventId == null || w.EventId == cmd.EventId))
            .OrderByDescending(w => w.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (latestWebhook is null) return new(false, "WEBHOOK_NOT_FOUND");
        if (latestWebhook.IsProcessed && cmd.EventId != null)
            return new(true, null); // idempotent replay — already processed

        // Mark as processed and update payment status
        latestWebhook.IsProcessed = true;
        latestWebhook.ProcessedAt = DateTime.UtcNow;
        payment.Status = PaymentStatus.Succeeded;

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.PaymentReplayWebhook,
            "Payment", cmd.PaymentId, reason: "Admin replayed webhook",
            after: new { WebhookId = latestWebhook.Id }, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── POST /admin/payments/{id}/mark-reconciled ─────────────────────────────────
// note. For a payment settled out of band.

public record MarkReconciledCommand(string PaymentId, string Note,
    string StaffId, string StaffName) : IRequest<FinanceCommandResult>;

public class MarkReconciledHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<MarkReconciledCommand, FinanceCommandResult>
{
    public async Task<FinanceCommandResult> Handle(MarkReconciledCommand cmd, CancellationToken ct)
    {
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == cmd.PaymentId, ct);
        if (payment is null) return new(false, "PAYMENT_NOT_FOUND");

        payment.IsReconciled       = true;
        payment.ReconciliationNote = cmd.Note;

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.PaymentMarkReconciled,
            "Payment", cmd.PaymentId, reason: cmd.Note, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}
