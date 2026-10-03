using Izigo.Application.Common.Interfaces;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Common.Helpers;

public static class RiderWalletPayments
{
    /// Rejects a wallet-paid booking up front when the rider can't cover the fare.
    public static async Task EnsureCanPayAsync(
        IApplicationDbContext db, string riderId, long amount, CancellationToken ct)
    {
        var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == riderId, ct);
        if (wallet == null || wallet.Balance < amount)
        {
            var balance = wallet?.Balance ?? 0;
            throw new InvalidOperationException(
                $"CONFLICT: Insufficient wallet balance ({balance} XOF) for this fare ({amount} XOF). Top up your wallet or choose another payment method.");
        }
        if (wallet.IsLocked)
            throw new InvalidOperationException(
                "CONFLICT: Your wallet is locked. Contact support or choose another payment method.");
    }

    public static async Task DebitAsync(
        IApplicationDbContext db, string riderId, long amount,
        string title, string? subtitle, string referenceId, CancellationToken ct)
    {
        var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == riderId, ct);
        if (wallet == null || amount <= 0) return;

        wallet.Balance -= amount;
        db.WalletTransactions.Add(new WalletTransaction
        {
            WalletId     = wallet.Id,
            Type         = WalletTransactionType.Trip,
            Amount       = amount,
            BalanceAfter = wallet.Balance,
            Title        = title,
            Subtitle     = subtitle,
            ReferenceId  = referenceId,
            Status       = PaymentStatus.Succeeded,
        });
    }
}
