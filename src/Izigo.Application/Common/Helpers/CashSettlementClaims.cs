using Izigo.Application.Common.Interfaces;
using Izigo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Common.Helpers;

/// <summary>
/// A driver can report that they paid their cash debt, but only ops reduce
/// it, once they have the money (Admin: record cash settlement). Until then
/// the report is a pending claim: shown to ops, never trusted.
/// </summary>
public static class CashSettlementClaims
{
    public const string Type = "cash_settlement_claim";

    public static Task<long> PendingAsync(IApplicationDbContext db, string driverWalletId, CancellationToken ct) =>
        db.DriverWalletTransactions
            .Where(t => t.DriverWalletId == driverWalletId && t.Type == Type && t.Status == PaymentStatus.Pending)
            .SumAsync(t => t.Amount, ct);

    /// <summary>Marks the oldest pending claims covered by money ops received.</summary>
    public static async Task ConfirmAsync(IApplicationDbContext db, string driverWalletId, long received, CancellationToken ct)
    {
        var claims = await db.DriverWalletTransactions
            .Where(t => t.DriverWalletId == driverWalletId && t.Type == Type && t.Status == PaymentStatus.Pending)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(ct);
        foreach (var claim in claims)
        {
            if (received < claim.Amount) break;
            received -= claim.Amount;
            claim.Status = PaymentStatus.Succeeded;
        }
    }
}
