using Izigo.Application.Common.Interfaces;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Admin.Finance.Commands;

// ── POST /admin/payouts/{id}/approve ─────────────────────────────────────────
// Permission: payouts.approve. Must be idempotent — double-approve = double payment.

public record ApprovePayoutCommand(string PayoutId, string StaffId, string StaffName, string Market)
    : IRequest<FinanceCommandResult>;

public class ApprovePayoutHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<ApprovePayoutCommand, FinanceCommandResult>
{
    public async Task<FinanceCommandResult> Handle(ApprovePayoutCommand cmd, CancellationToken ct)
    {
        var payout = await db.Payouts.FirstOrDefaultAsync(p => p.Id == cmd.PayoutId, ct);
        if (payout is null) return new(false, "PAYOUT_NOT_FOUND");

        // Idempotent: if already approved/processing/succeeded, replay success safely
        if (payout.Status is PaymentStatus.Succeeded or PaymentStatus.Processing
                          or PaymentStatus.Authorized)
            return new(true, null);

        if (payout.Status != PaymentStatus.Pending)
            return new(false, "PAYOUT_NOT_PENDING");

        var before = new { payout.Status };
        payout.Status      = PaymentStatus.Processing;
        payout.ApprovedBy  = cmd.StaffId;
        payout.ApprovedAt  = DateTime.UtcNow;

        // Create attempt record
        db.PayoutAttempts.Add(new Domain.Entities.PayoutAttempt
        {
            PayoutId = payout.Id,
            Status   = PaymentStatus.Processing
        });

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.PayoutApprove,
            "Payout", cmd.PayoutId, reason: "Admin approved payout",
            before: before, after: new { Status = "Processing" }, market: cmd.Market, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── POST /admin/payouts/{id}/reject ──────────────────────────────────────────
// reason. Returns funds to driver's available balance.

public record RejectPayoutCommand(string PayoutId, string Reason, string StaffId,
    string StaffName, string Market) : IRequest<FinanceCommandResult>;

public class RejectPayoutHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<RejectPayoutCommand, FinanceCommandResult>
{
    public async Task<FinanceCommandResult> Handle(RejectPayoutCommand cmd, CancellationToken ct)
    {
        var payout = await db.Payouts.FirstOrDefaultAsync(p => p.Id == cmd.PayoutId, ct);
        if (payout is null) return new(false, "PAYOUT_NOT_FOUND");
        if (payout.Status != PaymentStatus.Pending) return new(false, "PAYOUT_NOT_PENDING");

        var before = new { payout.Status };
        payout.Status          = PaymentStatus.Cancelled;
        payout.RejectionReason = cmd.Reason;

        // Return funds to driver's available balance
        var wallet = await db.DriverWallets.FirstOrDefaultAsync(w => w.DriverId == payout.DriverId, ct);
        if (wallet is not null)
        {
            wallet.AvailableBalance += payout.GrossAmount;
            db.DriverWalletTransactions.Add(new Domain.Entities.DriverWalletTransaction
            {
                DriverWalletId = wallet.Id,
                Type           = "payout_rejected",
                Amount         = payout.GrossAmount,
                BalanceAfter   = wallet.AvailableBalance,
                ReferenceId    = payout.Id,
                Reason         = cmd.Reason,
                CreatedBy      = cmd.StaffId
            });
        }

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.PayoutReject,
            "Payout", cmd.PayoutId, cmd.Reason, before,
            new { Status = "Cancelled" }, market: cmd.Market, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── POST /admin/payouts/{id}/retry ────────────────────────────────────────────

public record RetryPayoutCommand(string PayoutId, string StaffId, string StaffName, string Market)
    : IRequest<FinanceCommandResult>;

public class RetryPayoutHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<RetryPayoutCommand, FinanceCommandResult>
{
    public async Task<FinanceCommandResult> Handle(RetryPayoutCommand cmd, CancellationToken ct)
    {
        var payout = await db.Payouts.FirstOrDefaultAsync(p => p.Id == cmd.PayoutId, ct);
        if (payout is null) return new(false, "PAYOUT_NOT_FOUND");
        if (payout.Status != PaymentStatus.Failed) return new(false, "PAYOUT_NOT_FAILED");

        var before = new { payout.Status };
        payout.Status     = PaymentStatus.Processing;
        payout.ApprovedBy = cmd.StaffId;
        payout.ApprovedAt = DateTime.UtcNow;
        payout.FailureReason = null;

        db.PayoutAttempts.Add(new Domain.Entities.PayoutAttempt
        {
            PayoutId = payout.Id,
            Status   = PaymentStatus.Processing
        });

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.PayoutApprove,
            "Payout", cmd.PayoutId, reason: "Admin retry payout",
            before: before, after: new { Status = "Processing" }, market: cmd.Market, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── POST /admin/payouts/{id}/net-off-cash ────────────────────────────────────
// Settles driver's cash liability against this payout in one transaction.

public record NetOffCashCommand(string PayoutId, string StaffId, string StaffName, string Market)
    : IRequest<FinanceCommandResult>;

public class NetOffCashHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<NetOffCashCommand, FinanceCommandResult>
{
    public async Task<FinanceCommandResult> Handle(NetOffCashCommand cmd, CancellationToken ct)
    {
        var payout = await db.Payouts.FirstOrDefaultAsync(p => p.Id == cmd.PayoutId, ct);
        if (payout is null) return new(false, "PAYOUT_NOT_FOUND");
        if (payout.Status != PaymentStatus.Pending) return new(false, "PAYOUT_NOT_PENDING");

        var wallet = await db.DriverWallets.FirstOrDefaultAsync(w => w.DriverId == payout.DriverId, ct);
        if (wallet is null) return new(false, "DRIVER_WALLET_NOT_FOUND");
        if (wallet.PendingCashSettlement <= 0) return new(false, "NO_CASH_OWED");

        var before = new { payout.GrossAmount, payout.NetAmount, wallet.PendingCashSettlement };

        // Net off: reduce payout amount by cash owed (up to payout gross)
        var netOff = Math.Min(wallet.PendingCashSettlement, payout.GrossAmount);
        payout.GrossAmount -= netOff;
        payout.NetAmount   -= netOff;
        wallet.PendingCashSettlement -= netOff;

        if (payout.GrossAmount <= 0)
            payout.Status = PaymentStatus.Succeeded; // fully settled by cash

        db.DriverWalletTransactions.Add(new Domain.Entities.DriverWalletTransaction
        {
            DriverWalletId = wallet.Id,
            Type           = "cash_netoff",
            Amount         = -netOff,
            BalanceAfter   = wallet.PendingCashSettlement,
            ReferenceId    = payout.Id,
            Reason         = "Net off against payout",
            CreatedBy      = cmd.StaffId
        });

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.PayoutApprove,
            "Payout", cmd.PayoutId, reason: "Net off cash settlement",
            before: before, after: new { payout.GrossAmount, wallet.PendingCashSettlement },
            market: cmd.Market, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null, new { net_off_amount = netOff, remaining_payout = payout.GrossAmount });
    }
}

// ── POST /admin/payouts/bulk-approve ─────────────────────────────────────────
// ids[] → 202 + job id. Partial failure must report per-id outcomes.

public record BulkApprovePayoutsCommand(List<string> Ids, string StaffId, string StaffName,
    string Market) : IRequest<(string JobId, string StatusUrl)>;

public class BulkApprovePayoutsHandler(IApplicationDbContext db, IAuditService audit,
    IJobDispatcher jobDispatcher)
    : IRequestHandler<BulkApprovePayoutsCommand, (string JobId, string StatusUrl)>
{
    public async Task<(string JobId, string StatusUrl)> Handle(BulkApprovePayoutsCommand cmd, CancellationToken ct)
    {
        var job = new Domain.Entities.BackgroundJob
        {
            Type = "bulk_approve_payouts",
            Status = "queued",
            InitiatedByStaffId = cmd.StaffId
        };
        db.BackgroundJobs.Add(job);

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.PayoutApprove,
            "Payout", "bulk", reason: $"Bulk approve {cmd.Ids.Count} payouts",
            after: new { PayoutIds = cmd.Ids, JobId = job.Id },
            market: cmd.Market, ct: ct);

        await db.SaveChangesAsync(ct);
        jobDispatcher.Enqueue(job.Id, job.Type);
        return (job.Id, $"/api/v1/admin/jobs/{job.Id}");
    }
}

// ── POST /admin/cash-settlement/{driver_id}/record ────────────────────────────
// When a driver pays at an office: amount, method, receipt_ref.

public record RecordCashSettlementCommand(string DriverId, long Amount, string Method,
    string ReceiptRef, string StaffId, string StaffName, string Market) : IRequest<FinanceCommandResult>;

public class RecordCashSettlementHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<RecordCashSettlementCommand, FinanceCommandResult>
{
    public async Task<FinanceCommandResult> Handle(RecordCashSettlementCommand cmd, CancellationToken ct)
    {
        var wallet = await db.DriverWallets.FirstOrDefaultAsync(w => w.DriverId == cmd.DriverId, ct);
        if (wallet is null) return new(false, "DRIVER_NOT_FOUND");
        if (cmd.Amount <= 0) return new(false, "INVALID_AMOUNT");
        if (cmd.Amount > wallet.PendingCashSettlement) return new(false, "AMOUNT_EXCEEDS_OWED");

        var before = new { wallet.PendingCashSettlement };
        wallet.PendingCashSettlement -= cmd.Amount;

        db.DriverWalletTransactions.Add(new Domain.Entities.DriverWalletTransaction
        {
            DriverWalletId = wallet.Id,
            Type           = "cash_settlement",
            Amount         = -cmd.Amount,
            BalanceAfter   = wallet.PendingCashSettlement,
            ReferenceId    = cmd.ReceiptRef,
            Reason         = $"Cash received at office via {cmd.Method}. Ref: {cmd.ReceiptRef}",
            CreatedBy      = cmd.StaffId
        });

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.CashSettlementRecord,
            "Driver", cmd.DriverId, reason: $"Cash settled via {cmd.Method}, ref {cmd.ReceiptRef}",
            before: before, after: new { wallet.PendingCashSettlement }, market: cmd.Market, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── POST /admin/cash-settlement/{driver_id}/write-off ─────────────────────────
// amount, reason. High privilege — this is giving money away.

public record WriteOffCashSettlementCommand(string DriverId, long Amount, string Reason,
    string StaffId, string StaffName, string Market) : IRequest<FinanceCommandResult>;

public class WriteOffCashSettlementHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<WriteOffCashSettlementCommand, FinanceCommandResult>
{
    public async Task<FinanceCommandResult> Handle(WriteOffCashSettlementCommand cmd, CancellationToken ct)
    {
        var wallet = await db.DriverWallets.FirstOrDefaultAsync(w => w.DriverId == cmd.DriverId, ct);
        if (wallet is null) return new(false, "DRIVER_NOT_FOUND");
        if (cmd.Amount <= 0 || cmd.Amount > wallet.PendingCashSettlement)
            return new(false, "INVALID_AMOUNT");

        var before = new { wallet.PendingCashSettlement };
        wallet.PendingCashSettlement -= cmd.Amount;

        db.DriverWalletTransactions.Add(new Domain.Entities.DriverWalletTransaction
        {
            DriverWalletId = wallet.Id,
            Type           = "write_off",
            Amount         = -cmd.Amount,
            BalanceAfter   = wallet.PendingCashSettlement,
            Reason         = cmd.Reason,
            CreatedBy      = cmd.StaffId
        });

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.PayoutWriteOff,
            "Driver", cmd.DriverId, cmd.Reason,
            before, new { wallet.PendingCashSettlement }, market: cmd.Market, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}
