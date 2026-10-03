using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Quotes.Helpers;
using Izigo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.CoRide.Helpers;

/// <summary>
/// A co-ride seat is the fare for the driver's whole trip, split by the
/// seats they offer. The share is fixed at publish time, so a rider who
/// booked early does not pay more when other seats stay empty.
/// </summary>
public static class CoRideFare
{
    public readonly record struct SeatSplit(long TripFare, long PricePerSeat, string Currency, int DistanceM);

    public static long Share(long tripFare, int seats)
    {
        if (seats < 1) return tripFare;
        return (tripFare + seats - 1) / seats;
    }

    public static async Task<SeatSplit> QuoteAsync(
        IApplicationDbContext db,
        IGeoService geo,
        double fromLat, double fromLng,
        double toLat, double toLng,
        int seats,
        CancellationToken ct)
    {
        if (seats is < 1 or > 8)
            throw new ArgumentException("VALIDATION_ERROR: Seats must be between 1 and 8.");

        var route = await geo.GetRouteAsync(
            new GeoPoint(fromLat, fromLng), new GeoPoint(toLat, toLng), null, "driving", ct);
        var distanceM = route?.DistanceM ?? StraightLine(fromLat, fromLng, toLat, toLng);
        var freeS = route?.DurationS ?? Math.Max(60, distanceM / 8);

        var config = await db.PlatformConfigs.FirstOrDefaultAsync(ct);
        var market = string.IsNullOrWhiteSpace(config?.Market) ? "ci" : config!.Market;
        var currency = string.IsNullOrWhiteSpace(config?.Currency) ? "GNF" : config!.Currency;

        var rule = await db.FareRules
            .Where(r => r.IsActive && r.ServiceClass == ServiceClass.ZenCoRide &&
                        (r.Market == market || r.Market == ""))
            .OrderByDescending(r => r.Version)
            .FirstOrDefaultAsync(ct)
            ?? FareCalculator.DefaultRule(ServiceClass.ZenCoRide);

        var fare = FareCalculator.Calculate(rule, distanceM, freeS, freeS);
        return new SeatSplit(fare.ListTotal, Share(fare.ListTotal, seats), currency, distanceM);
    }

    private static int StraightLine(double lat1, double lng1, double lat2, double lng2)
    {
        const double r = 6_371_000;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLng = (lng2 - lng1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
              * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return (int)(r * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a)));
    }
}
