using Izigo.Application.Common.Helpers;
using Izigo.Application.Common.Interfaces;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Rides.Helpers;

/// <summary>
/// The one place an in-app tip moves money: the rider's wallet pays it and
/// the driver's wallet receives it, in the same save. Cash tips are handed
/// over in person and never pass through here.
/// </summary>
public static class RideTips
{
    public const long MaxTip = 10_000;

    public static async Task ApplyAsync(IApplicationDbContext db, Trip trip, long amount, CancellationToken ct)
    {
        if (amount <= 0 || amount > MaxTip)
            throw new ArgumentException($"VALIDATION_ERROR: A tip must be between 1 and {MaxTip} {trip.Currency}.");

        if (trip.JobState != JobState.Completed)
            throw new InvalidOperationException("CONFLICT: Can only tip on completed trips.");

        // One tip per trip: a second request must not pay the driver again.
        if (trip.FareTip > 0)
            throw new InvalidOperationException("CONFLICT: You have already tipped on this trip.");

        var config = await db.PlatformConfigs.FirstOrDefaultAsync(c => c.Market == trip.Market, ct);
        if (config is not null && !config.TippingEnabled)
            throw new InvalidOperationException("CONFLICT: Tipping is not enabled in this market.");

        var driverWallet = trip.DriverId is null
            ? null
            : await db.DriverWallets.FirstOrDefaultAsync(w => w.DriverId == trip.DriverId, ct);
        if (driverWallet is null)
            throw new InvalidOperationException("CONFLICT: This trip's driver cannot receive tips right now.");

        await RiderWalletPayments.EnsureCanPayAsync(db, trip.RiderId, amount, ct);
        await RiderWalletPayments.DebitAsync(
            db, trip.RiderId, amount, "Tip for your driver", trip.Code, trip.Id, ct);

        trip.FareTip = amount;
        driverWallet.AvailableBalance += amount;
        db.DriverWalletTransactions.Add(new DriverWalletTransaction
        {
            DriverWalletId = driverWallet.Id,
            Type           = "tip",
            Amount         = amount,
            BalanceAfter   = driverWallet.AvailableBalance,
            ReferenceId    = trip.Id,
        });
    }
}
