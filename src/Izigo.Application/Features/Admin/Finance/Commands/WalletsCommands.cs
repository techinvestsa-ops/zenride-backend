using Izigo.Application.Common.Interfaces;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Admin.Finance.Commands;

// ── POST /admin/wallets/{id}/adjust ──────────────────────────────────────────
// direction=credit|debit, amount, reason (required), Idempotency-Key
// Permission: wallets.adjust

public record AdjustWalletCommand(string WalletId, string Direction, long Amount,
    string Reason, string IdempotencyKey, string StaffId, string StaffName)
    : IRequest<FinanceCommandResult>;

public class AdjustWalletHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<AdjustWalletCommand, FinanceCommandResult>
{
    public async Task<FinanceCommandResult> Handle(AdjustWalletCommand cmd, CancellationToken ct)
    {
        // Idempotency check
        var existing = await db.IdempotencyRecords
            .FirstOrDefaultAsync(r => r.Key == cmd.IdempotencyKey && r.Endpoint == "admin.wallet.adjust", ct);
        if (existing is not null) return new(true, null);

        if (cmd.Amount <= 0) return new(false, "INVALID_AMOUNT");
        var signed = cmd.Direction == "credit" ? cmd.Amount : -cmd.Amount;

        // Rider wallet (wlt_ prefix)
        if (cmd.WalletId.StartsWith("wlt_"))
        {
            var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.Id == cmd.WalletId, ct);
            if (wallet is null) return new(false, "WALLET_NOT_FOUND");

            var before = new { wallet.Balance };
            wallet.Balance += signed;

            db.WalletTransactions.Add(new Domain.Entities.WalletTransaction
            {
                WalletId    = wallet.Id,
                Type        = WalletTransactionType.Adjustment,
                Amount      = signed,
                BalanceAfter = wallet.Balance,
                Title       = $"Admin {cmd.Direction}",
                Reason      = cmd.Reason,
                CreatedBy   = cmd.StaffId
            });

            await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.WalletAdjust,
                "Wallet", cmd.WalletId, cmd.Reason,
                before, new { wallet.Balance }, ct: ct);
        }
        else if (cmd.WalletId.StartsWith("dwl_"))
        {
            var wallet = await db.DriverWallets.FirstOrDefaultAsync(w => w.Id == cmd.WalletId, ct);
            if (wallet is null) return new(false, "WALLET_NOT_FOUND");

            var before = new { wallet.AvailableBalance };
            wallet.AvailableBalance += signed;

            db.DriverWalletTransactions.Add(new Domain.Entities.DriverWalletTransaction
            {
                DriverWalletId = wallet.Id,
                Type           = "adjustment",
                Amount         = signed,
                BalanceAfter   = wallet.AvailableBalance,
                Reason         = cmd.Reason,
                CreatedBy      = cmd.StaffId
            });

            await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.WalletAdjust,
                "DriverWallet", cmd.WalletId, cmd.Reason,
                before, new { wallet.AvailableBalance }, ct: ct);
        }
        else
        {
            return new(false, "WALLET_NOT_FOUND");
        }

        db.IdempotencyRecords.Add(new Domain.Entities.IdempotencyRecord
        {
            Key = cmd.IdempotencyKey, Endpoint = "admin.wallet.adjust",
            ResponseJson = $"{{\"wallet_id\":\"{cmd.WalletId}\"}}",
            StatusCode = 200, ExpiresAt = DateTime.UtcNow.AddDays(7)
        });

        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── POST /admin/wallets/{id}/freeze ──────────────────────────────────────────
// reason. Blocks spend and withdrawal, not earning.

public record FreezeWalletCommand(string WalletId, string Reason, string StaffId, string StaffName)
    : IRequest<FinanceCommandResult>;

public class FreezeWalletHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<FreezeWalletCommand, FinanceCommandResult>
{
    public async Task<FinanceCommandResult> Handle(FreezeWalletCommand cmd, CancellationToken ct)
    {
        if (cmd.WalletId.StartsWith("wlt_"))
        {
            var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.Id == cmd.WalletId, ct);
            if (wallet is null) return new(false, "WALLET_NOT_FOUND");

            var before = new { wallet.IsLocked };
            wallet.IsLocked   = !wallet.IsLocked; // toggle freeze/unfreeze
            wallet.LockReason = wallet.IsLocked ? cmd.Reason : null;

            await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.WalletFreeze,
                "Wallet", cmd.WalletId, cmd.Reason,
                before, new { wallet.IsLocked }, ct: ct);
        }
        else if (cmd.WalletId.StartsWith("dwl_"))
        {
            var wallet = await db.DriverWallets.FirstOrDefaultAsync(w => w.Id == cmd.WalletId, ct);
            if (wallet is null) return new(false, "WALLET_NOT_FOUND");

            var before = new { wallet.IsLocked };
            wallet.IsLocked   = !wallet.IsLocked;
            wallet.LockReason = wallet.IsLocked ? cmd.Reason : null;

            await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.WalletFreeze,
                "DriverWallet", cmd.WalletId, cmd.Reason,
                before, new { wallet.IsLocked }, ct: ct);
        }
        else
        {
            return new(false, "WALLET_NOT_FOUND");
        }

        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}
