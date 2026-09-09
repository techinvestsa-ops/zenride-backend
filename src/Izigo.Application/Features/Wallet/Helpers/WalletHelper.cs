using Izigo.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using WalletEntity = Izigo.Domain.Entities.Wallet;
using DriverWalletEntity = Izigo.Domain.Entities.DriverWallet;

namespace Izigo.Application.Features.Wallets;

internal static class WalletHelper
{
    public static async Task<WalletEntity> GetOrCreateAsync(
        string userId, IApplicationDbContext db, CancellationToken ct)
    {
        var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == userId, ct);
        if (wallet != null) return wallet;
        wallet = new WalletEntity { UserId = userId };
        db.Wallets.Add(wallet);
        await db.SaveChangesAsync(ct);
        return wallet;
    }

    public static async Task<DriverWalletEntity> GetOrCreateDriverAsync(
        string driverId, IApplicationDbContext db, CancellationToken ct)
    {
        var wallet = await db.DriverWallets.FirstOrDefaultAsync(w => w.DriverId == driverId, ct);
        if (wallet != null) return wallet;
        wallet = new DriverWalletEntity { DriverId = driverId };
        db.DriverWallets.Add(wallet);
        await db.SaveChangesAsync(ct);
        return wallet;
    }
}
