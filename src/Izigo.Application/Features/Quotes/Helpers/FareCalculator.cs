using Izigo.Domain.Entities;
using Izigo.Domain.Enums;

namespace Izigo.Application.Features.Quotes.Helpers;

public record FareBreakdown(
    long Total,
    long Base,
    long Distance,
    long Time,
    long ServiceFee,
    long Discount
);

public static class FareCalculator
{
    public static FareBreakdown Calculate(
        FareRule rule,
        int distanceM,
        int durationS,
        decimal surgeMultiplier = 1.0m,
        long promoDiscount = 0,
        decimal serviceFeeRate = 0.08m)
    {
        var distanceKm = distanceM / 1000m;
        var durationMin = durationS / 60m;

        var baseAmount = rule.Base;
        var distanceAmount = (long)Math.Round(distanceKm * rule.PerKm);
        var timeAmount = (long)Math.Round(durationMin * rule.PerMin);

        var subtotal = baseAmount + distanceAmount + timeAmount;
        // Apply surge
        subtotal = (long)Math.Round(subtotal * surgeMultiplier);
        // Apply minimum fare
        subtotal = Math.Max(subtotal, rule.Minimum);

        var serviceFee = (long)Math.Round(subtotal * serviceFeeRate);
        var total = subtotal + serviceFee - promoDiscount;

        return new FareBreakdown(
            Total: Math.Max(0, total),
            Base: baseAmount,
            Distance: distanceAmount,
            Time: timeAmount,
            ServiceFee: serviceFee,
            Discount: promoDiscount
        );
    }

    /// <summary>Returns the default fare rule for a service class when none is in the DB.</summary>
    public static FareRule DefaultRule(ServiceClass sc) => new()
    {
        ServiceClass = sc,
        Base = sc switch
        {
            ServiceClass.ZenCar     => 500,
            ServiceClass.ZenBike    => 300,
            ServiceClass.ZenCoRide  => 250,
            ServiceClass.PackageSmall => 400,
            ServiceClass.PackageLarge => 600,
            _ => 400
        },
        PerKm = sc switch
        {
            ServiceClass.ZenCar     => 250,
            ServiceClass.ZenBike    => 180,
            ServiceClass.ZenCoRide  => 190,
            ServiceClass.PackageSmall => 200,
            ServiceClass.PackageLarge => 280,
            _ => 200
        },
        PerMin = sc switch
        {
            ServiceClass.ZenCar     => 15,
            ServiceClass.ZenBike    => 10,
            ServiceClass.ZenCoRide  => 10,
            ServiceClass.PackageSmall => 12,
            ServiceClass.PackageLarge => 12,
            _ => 12
        },
        Minimum = sc switch
        {
            ServiceClass.ZenCar     => 1200,
            ServiceClass.ZenBike    => 800,
            ServiceClass.ZenCoRide  => 600,
            ServiceClass.PackageSmall => 900,
            ServiceClass.PackageLarge => 1500,
            _ => 1000
        },
        WaitingPerMin = 20,
        CancellationFee = sc is ServiceClass.ZenCar ? 500 : 300
    };
}
