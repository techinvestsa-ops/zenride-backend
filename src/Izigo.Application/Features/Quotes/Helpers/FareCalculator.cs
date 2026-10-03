using Izigo.Domain.Entities;
using Izigo.Domain.Enums;

namespace Izigo.Application.Features.Quotes.Helpers;

public record FareBreakdown(
    long Total,
    long Base,
    long Distance,
    long Time,
    long ServiceFee,
    long Discount,
    long ListTotal = 0,
    long Traffic = 0);

public static class FareCalculator
{
    public const int MaxTrafficPercent = 20;

    /// <summary>
    /// Rider total is the admin base fare plus the distance charge.
    /// When traffic makes the drive more than the admin threshold longer
    /// than the free-flow time, a small percent of that fare is added.
    /// </summary>
    public static FareBreakdown Calculate(
        FareRule rule,
        int distanceM,
        int freeFlowDurationS,
        int trafficDurationS,
        decimal surgeMultiplier = 1.0m,
        long promoDiscount = 0)
    {
        var distanceKm = distanceM / 1000m;
        var baseAmount = rule.Base;
        var distanceAmount = (long)Math.Round(distanceKm * rule.PerKm);

        var subtotal = baseAmount + distanceAmount;
        subtotal = (long)Math.Round(subtotal * (surgeMultiplier <= 0 ? 1m : surgeMultiplier));
        subtotal = Math.Max(subtotal, rule.Minimum);

        var delayMin = Math.Max(0, (trafficDurationS - freeFlowDurationS) / 60);
        var threshold = rule.TrafficDelayMin <= 0 ? 15 : rule.TrafficDelayMin;
        var percent = Math.Clamp(rule.TrafficPercent, 0m, MaxTrafficPercent);
        long traffic = 0;
        if (delayMin > threshold && percent > 0)
            traffic = (long)Math.Round(subtotal * percent / 100m);

        var listTotal = subtotal + traffic;
        var discount = Math.Clamp(promoDiscount, 0, listTotal);
        var total = listTotal - discount;

        return new FareBreakdown(
            Total: total,
            Base: baseAmount,
            Distance: distanceAmount,
            Time: traffic,
            ServiceFee: 0,
            Discount: discount,
            ListTotal: listTotal,
            Traffic: traffic);
    }

    public static FareRule DefaultRule(ServiceClass sc) => new()
    {
        ServiceClass = sc,
        Base = sc switch
        {
            ServiceClass.ZenCar     => 2_000,
            ServiceClass.ZenBike    => 1_000,
            ServiceClass.ZenCoRide  => 500,
            ServiceClass.PackageSmall => 1_500,
            ServiceClass.PackageLarge => 2_500,
            _ => 1_500
        },
        PerKm = sc switch
        {
            ServiceClass.ZenCar     => 3_000,
            ServiceClass.ZenBike    => 1_800,
            ServiceClass.ZenCoRide  => 800,
            ServiceClass.PackageSmall => 2_200,
            ServiceClass.PackageLarge => 3_200,
            _ => 2_200
        },
        PerMin = 0,
        Minimum = sc switch
        {
            ServiceClass.ZenCar     => 7_000,
            ServiceClass.ZenBike    => 4_000,
            ServiceClass.ZenCoRide  => 2_000,
            ServiceClass.PackageSmall => 5_000,
            ServiceClass.PackageLarge => 8_000,
            _ => 5_000
        },
        WaitingPerMin = 20,
        CancellationFee = sc is ServiceClass.ZenCar ? 500 : 300,
        TrafficDelayMin = 15,
        TrafficPercent = 8m
    };
}
