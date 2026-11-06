using Izigo.Application.Common.Interfaces;
using Izigo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Common.Helpers;

/// <summary>
/// Driver wallets are created keyed by the driver's user id, but nothing set
/// the profile's DriverWallet link, so code reading dp.DriverWallet saw no
/// wallet: drivers were never credited for non-cash trips and cash-trip
/// commission was never recorded as owed. Always resolve through here.
/// </summary>
public static class DriverWallets
{
    /// <summary>The driver's wallet, found by driver id (or created), and linked to the profile.</summary>
    public static async Task<DriverWallet> ForAsync(IApplicationDbContext db, DriverProfile dp, CancellationToken ct)
    {
        if (dp.DriverWallet is { } linked) return linked;
        var wallet = await db.DriverWallets.FirstOrDefaultAsync(w => w.DriverId == dp.UserId, ct);
        if (wallet is null)
        {
            wallet = new DriverWallet { DriverId = dp.UserId };
            db.DriverWallets.Add(wallet);
        }
        dp.DriverWallet = wallet;
        return wallet;
    }

    /// <summary>Read-only: the driver's wallet if one exists, without creating or linking.</summary>
    public static async Task<DriverWallet?> FindAsync(IApplicationDbContext db, DriverProfile dp, CancellationToken ct) =>
        dp.DriverWallet ?? await db.DriverWallets.AsNoTracking().FirstOrDefaultAsync(w => w.DriverId == dp.UserId, ct);
}
