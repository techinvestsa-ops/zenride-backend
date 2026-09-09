using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Settings;
using Izigo.Application.Features.Quotes.Dtos;
using Izigo.Application.Features.Quotes.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Izigo.Application.Features.Quotes.Commands;

public record CreateQuoteCommand(string UserId, CreateQuoteRequest Request) : IRequest<QuoteDto>;

public class CreateQuoteHandler(
    IApplicationDbContext db,
    IGeoService geo,
    IOptions<QuoteSettings> quoteOptions)
    : IRequestHandler<CreateQuoteCommand, QuoteDto>
{
    private static readonly string[] DefaultPaymentMethods =
        ["cash", "wallet", "orange_money", "moov_money", "wave"];

    public async Task<QuoteDto> Handle(CreateQuoteCommand req, CancellationToken ct)
    {
        var cfg = quoteOptions.Value;
        if (!Enum.TryParse<Vertical>(req.Request.Vertical, true, out var vertical))
            throw new ArgumentException("VALIDATION_ERROR: Invalid vertical.");

        var pickup = req.Request.Pickup;
        var dropoff = req.Request.Dropoff;

        // Get route from geo service (distance, duration, polyline)
        var route = await geo.GetRouteAsync(
            new GeoPoint(pickup.Lat, pickup.Lng),
            new GeoPoint(dropoff.Lat, dropoff.Lng),
            null, "driving", ct);

        // Fall back to straight-line if geo stub returns null
        var distanceM = route?.DistanceM ?? EstimateStraightLine(pickup.Lat, pickup.Lng, dropoff.Lat, dropoff.Lng);
        var durationS  = route?.DurationS ?? (int)(distanceM / cfg.FallbackSpeedMs);
        var polyline   = route?.EncodedPolyline;

        // Load active fare rules for this market
        var fareRules = await db.FareRules
            .Where(r => r.IsActive)
            .ToListAsync(ct);

        // Load surge for the pickup zone (if any)
        decimal surgeMultiplier = 1.0m;
        string? surgeReason = null;

        // Load payment methods from config
        var config = await db.PlatformConfigs.FirstOrDefaultAsync(ct);
        var paymentMethods = DefaultPaymentMethods;

        // Determine which service classes to quote
        ServiceClass[] classes = vertical switch
        {
            Vertical.Ride    => [ServiceClass.ZenCar, ServiceClass.ZenBike],
            Vertical.CoRide  => [ServiceClass.ZenCoRide],
            Vertical.Package => [ServiceClass.PackageSmall, ServiceClass.PackageLarge],
            _                => [ServiceClass.ZenCar]
        };

        // Build options — one per service class
        var options = classes.Select(sc =>
        {
            var rule = fareRules.FirstOrDefault(r => r.ServiceClass == sc)
                       ?? FareCalculator.DefaultRule(sc);
            var fare = FareCalculator.Calculate(rule, distanceM, durationS, surgeMultiplier,
                serviceFeeRate: cfg.ServiceFeeRate);
            var etaMin  = EstimatePickupEta(sc);
            var durationMin = (int)Math.Ceiling(durationS / 60.0);

            return new QuoteOptionDto(
                ClassCode: ToClassCode(sc),
                Name: ToClassName(sc),
                Icon: ToIcon(sc),
                Seats: ToSeatCount(sc),
                EtaPickupMin: etaMin,
                DurationMin: durationMin,
                Available: true,
                Fare: new QuoteFareDto(fare.Total, fare.Base, fare.Distance,
                    fare.Time, fare.ServiceFee, fare.Discount));
        }).ToArray();

        // Persist the quote so trips can be created from quote_id
        var optionsJson = System.Text.Json.JsonSerializer.Serialize(options);
        var quote = new Quote
        {
            RiderId = req.UserId,
            Vertical = vertical,
            PickupLat = (decimal)pickup.Lat,
            PickupLng = (decimal)pickup.Lng,
            PickupLabel = pickup.Label,
            DropoffLat = (decimal)dropoff.Lat,
            DropoffLng = (decimal)dropoff.Lng,
            DropoffLabel = dropoff.Label,
            DistanceM = distanceM,
            DurationS = durationS,
            EncodedPolyline = polyline,
            OptionsJson = optionsJson,
            SurgeActive = surgeMultiplier > 1.0m,
            SurgeMultiplier = surgeMultiplier,
            SurgeReason = surgeReason,
            PromoCode = req.Request.PromoCode,
            Currency = "XOF",
            ExpiresAt = DateTime.UtcNow.AddMinutes(cfg.ExpiryMinutes)
        };
        db.Quotes.Add(quote);
        await db.SaveChangesAsync(ct);

        return new QuoteDto(
            QuoteId: quote.Id,
            ExpiresAt: quote.ExpiresAt,
            Currency: quote.Currency,
            Pickup: new QuoteLocationDto(pickup.Label, pickup.Lat, pickup.Lng, pickup.PlaceId),
            Dropoff: new QuoteLocationDto(dropoff.Label, dropoff.Lat, dropoff.Lng, dropoff.PlaceId),
            DistanceM: distanceM,
            DurationS: durationS,
            EncodedPolyline: polyline,
            Surge: new SurgeDto(quote.SurgeActive, (double)surgeMultiplier, surgeReason),
            Options: options,
            PaymentMethods: paymentMethods);
    }

    // Rough straight-line estimate as fallback when geo service is not yet wired
    private static int EstimateStraightLine(double lat1, double lng1, double lat2, double lng2)
    {
        const double R = 6371000; // Earth radius in metres
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLng = (lng2 - lng1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
              * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return (int)(R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a)));
    }

    // Simplified ETA estimate until real dispatch is implemented
    private static int EstimatePickupEta(ServiceClass sc) => sc switch
    {
        ServiceClass.ZenBike    => 3,
        ServiceClass.ZenCoRide  => 7,
        ServiceClass.PackageSmall or ServiceClass.PackageLarge => 8,
        _                       => 5
    };

    private static string ToClassCode(ServiceClass sc) => sc switch
    {
        ServiceClass.ZenCar     => "zen_car",
        ServiceClass.ZenBike    => "zen_bike",
        ServiceClass.ZenCoRide  => "zen_coride",
        ServiceClass.PackageSmall => "package_small",
        ServiceClass.PackageLarge => "package_large",
        _ => sc.ToString().ToLower()
    };

    private static string ToClassName(ServiceClass sc) => sc switch
    {
        ServiceClass.ZenCar     => "Zen Car",
        ServiceClass.ZenBike    => "Zen Bike",
        ServiceClass.ZenCoRide  => "Zen Co-Ride",
        ServiceClass.PackageSmall => "Small Package",
        ServiceClass.PackageLarge => "Large Package",
        _ => sc.ToString()
    };

    private static string ToIcon(ServiceClass sc) => sc switch
    {
        ServiceClass.ZenCar     => "car",
        ServiceClass.ZenBike    => "bike",
        ServiceClass.ZenCoRide  => "group",
        ServiceClass.PackageSmall => "package",
        ServiceClass.PackageLarge => "box",
        _ => "car"
    };

    private static int ToSeatCount(ServiceClass sc) => sc switch
    {
        ServiceClass.ZenCar     => 4,
        ServiceClass.ZenBike    => 1,
        ServiceClass.ZenCoRide  => 6,
        _ => 0
    };
}
