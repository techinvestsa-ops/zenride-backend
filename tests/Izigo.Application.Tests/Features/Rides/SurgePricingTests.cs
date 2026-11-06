using FluentAssertions;
using Izigo.Application.Features.Quotes.Helpers;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Xunit;

namespace Izigo.Application.Tests.Features.Rides;

/// <summary>Surge is off unless switched on, rises only in a real rush, and is capped.</summary>
public class SurgePricingTests
{
    [Theory]
    [InlineData(4, 4, 1.0)]   // as many drivers as requests
    [InlineData(6, 4, 1.1)]   // 1.5 requests per driver
    [InlineData(8, 4, 1.2)]
    [InlineData(40, 1, 1.5)]  // capped
    [InlineData(5, 0, 1.5)]   // nobody free: counts as one driver, capped
    public void Multiplier_steps_with_demand_and_is_capped(int demand, int supply, double expected)
    {
        SurgePricing.Multiplier(demand, supply).Multiplier.Should().Be((decimal)expected);
    }

    private static async Task<Izigo.Infrastructure.Persistence.ApplicationDbContext> Rush(bool flagOn)
    {
        var db = DbContextFactory.Create();
        db.FeatureFlags.Add(new FeatureFlag { Key = SurgePricing.FlagKey, Market = "ci", Enabled = flagOn });
        for (var i = 0; i < 8; i++)
            db.Trips.Add(new Trip
            {
                RiderId = $"usr_{i}", JobState = JobState.Broadcasting, Vertical = Vertical.Ride, Market = "ci",
                Currency = "XOF", ServiceClass = ServiceClass.ZenCar, PickupLat = 5.3236m, PickupLng = -4.0205m,
            });
        db.DriverProfiles.Add(new DriverProfile
        {
            UserId = "usr_free", IsOnline = true, KycStatus = KycStatus.Approved,
            LastLat = 5.3240m, LastLng = -4.0210m, LastLocationAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task A_rush_with_the_flag_on_raises_the_price()
    {
        using var db = await Rush(flagOn: true);
        var (m, reason) = await SurgePricing.ForPickupAsync(db, "ci", 5.3236, -4.0205, 5, default);
        m.Should().BeGreaterThan(1.0m);
        reason.Should().NotBeNull();
    }

    [Fact]
    public async Task The_same_rush_with_the_flag_off_does_not()
    {
        using var db = await Rush(flagOn: false);
        var (m, _) = await SurgePricing.ForPickupAsync(db, "ci", 5.3236, -4.0205, 5, default);
        m.Should().Be(1.0m);
    }
}
