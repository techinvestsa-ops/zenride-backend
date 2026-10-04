using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.CoRide.Helpers;

internal readonly record struct DriverFix(decimal Lat, decimal Lng);

internal static class CoRidePresence
{
    private static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(5);

    public static string NewBoardingCode(IReadOnlyCollection<string> taken)
    {
        for (var i = 0; i < 50; i++)
        {
            var code = Random.Shared.Next(100, 1000).ToString();
            if (!taken.Contains(code)) return code;
        }
        throw new InvalidOperationException("CONFLICT: No boarding code is free on this trip.");
    }

    public static async Task<DriverFix> RequireFixAsync(
        IApplicationDbContext db, string driverId, CancellationToken ct)
    {
        var fix = await LatestFixAsync(db, driverId, ct);
        if (fix is null)
            throw new InvalidOperationException(
                "CONFLICT: Share your live location before confirming this passenger.");
        return fix.Value;
    }

    public static async Task<DriverFix?> LatestFixAsync(
        IApplicationDbContext db, string driverId, CancellationToken ct)
    {
        var dp = await db.DriverProfiles.AsNoTracking()
            .FirstOrDefaultAsync(d => d.UserId == driverId, ct);
        if (dp?.LastLat is not decimal lat || dp.LastLng is not decimal lng) return null;
        if (dp.LastLocationAt is null || dp.LastLocationAt < DateTime.UtcNow - FreshFor) return null;
        return new DriverFix(lat, lng);
    }

    public static async Task AlightAsync(
        IApplicationDbContext db,
        CoRideBooking booking,
        CoRideListing listing,
        DriverFix? fix,
        CancellationToken ct)
    {
        if (booking.AlightedAt == null)
        {
            booking.AlightedAt = DateTime.UtcNow;
            if (fix is DriverFix point)
            {
                booking.AlightLat = point.Lat;
                booking.AlightLng = point.Lng;
            }
        }

        booking.Status = CoRideBookingStatus.Completed;
        if (booking.PaymentMethod == PaymentMethod.Wallet)
            await RiderWalletPayments.DebitAsync(db, booking.RiderId, booking.Total,
                $"Co-ride to {listing.ToLabel}", listing.Id, booking.Id, ct);

        await db.SaveChangesAsync(ct);
    }
}
