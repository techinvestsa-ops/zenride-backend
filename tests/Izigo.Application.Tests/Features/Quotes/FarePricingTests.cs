using FluentAssertions;
using Izigo.Application.Features.Quotes.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Xunit;

namespace Izigo.Application.Tests.Features.Quotes;

public class FarePricingTests
{
    private static FareRule Rule() => new()
    {
        ServiceClass = ServiceClass.ZenCar,
        Base = 5_000,
        PerKm = 2_000,
        Minimum = 5_000,
        TrafficDelayMin = 15,
        TrafficPercent = 10m
    };

    [Fact]
    public void Total_IsBasePlusDistance_InTheAdminCurrency()
    {
        var fare = FareCalculator.Calculate(Rule(), distanceM: 10_000, freeFlowDurationS: 600, trafficDurationS: 600);

        fare.Base.Should().Be(5_000);
        fare.Distance.Should().Be(20_000);
        fare.Traffic.Should().Be(0);
        fare.ListTotal.Should().Be(25_000);
        fare.Total.Should().Be(25_000);
    }

    [Fact]
    public void Traffic_AddsASmallPercent_OnlyAfterTheAdminThreshold()
    {
        var under = FareCalculator.Calculate(Rule(), 10_000, freeFlowDurationS: 600, trafficDurationS: 600 + 10 * 60);
        var over = FareCalculator.Calculate(Rule(), 10_000, freeFlowDurationS: 600, trafficDurationS: 600 + 20 * 60);

        under.Traffic.Should().Be(0);
        under.Total.Should().Be(25_000);
        over.Traffic.Should().Be(2_500);
        over.Total.Should().Be(27_500);
    }

    [Fact]
    public void TrafficPercent_CannotExceedTwenty()
    {
        var rule = Rule();
        rule.TrafficPercent = 80m;

        var fare = FareCalculator.Calculate(rule, 10_000, 600, 600 + 30 * 60);

        fare.Traffic.Should().Be(5_000);
    }

    [Fact]
    public void Promo_ReducesTheTotal_AndKeepsTheOriginal()
    {
        var now = DateTime.UtcNow;
        var promo = new Coupon
        {
            Code = "LIVE10",
            Title = "Afternoon promo",
            DiscountType = "percent",
            Value = 10,
            AutoApply = true,
            IsActive = true,
            FirstTripOnly = false,
            VerticalsJson = "[]"
        };

        var preview = FareCalculator.Calculate(Rule(), 10_000, 600, 600);
        var applied = PromoPricing.Best(preview.ListTotal, [promo], riderHasCompletedTrip: true, null, "ride", now);
        var fare = FareCalculator.Calculate(Rule(), 10_000, 600, 600, promoDiscount: applied.Discount);

        applied.Discount.Should().Be(2_500);
        fare.ListTotal.Should().Be(25_000);
        fare.Total.Should().Be(22_500);
    }

    [Fact]
    public void NewUserPromo_DoesNotApply_AfterTheFirstCompletedTrip()
    {
        var promo = new Coupon
        {
            Code = "WELCOME",
            DiscountType = "percent",
            Value = 25,
            AutoApply = true,
            IsActive = true,
            FirstTripOnly = true,
            VerticalsJson = "[]"
        };

        var fresh = PromoPricing.Best(10_000, [promo], riderHasCompletedTrip: false, null, "ride", DateTime.UtcNow);
        var returning = PromoPricing.Best(10_000, [promo], riderHasCompletedTrip: true, null, "ride", DateTime.UtcNow);

        fresh.Discount.Should().Be(2_500);
        returning.Discount.Should().Be(0);
    }
}
