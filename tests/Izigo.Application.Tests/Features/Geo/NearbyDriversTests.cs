using FluentAssertions;
using Izigo.Application.Features.Geo.Queries;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Xunit;

namespace Izigo.Application.Tests.Features.Geo;

/// <summary>
/// The rider's map animates nearby cars, so each driver must keep the same
/// token and offset between calls, while never exposing the exact position.
/// </summary>
public class NearbyDriversTests
{
    private static User Driver(decimal lat, decimal lng, KycStatus kyc = KycStatus.Approved)
    {
        var user = new User { Status = UserStatus.Active };
        user.DriverProfile = new DriverProfile
        {
            UserId = user.Id, IsOnline = true, KycStatus = kyc, OnboardingComplete = true,
            LastLat = lat, LastLng = lng, LastHeading = 90, LastLocationAt = DateTime.UtcNow,
            VerticalsAllowed = [Vertical.Ride],
        };
        return user;
    }

    private static GetNearbyDriversHandler Handler(global::Izigo.Infrastructure.Persistence.ApplicationDbContext db) =>
        new(db, FakeServices.OpsSettings(o => o.NearbyDriverTokenKey = "test-key"));

    [Fact]
    public async Task SameDriver_KeepsItsTokenAndPosition_BetweenCalls()
    {
        using var db = DbContextFactory.Create();
        var driver = Driver(9.5440m, -13.6600m);
        db.Users.Add(driver);
        await db.SaveChangesAsync();
        var query = new GetNearbyDriversQuery(9.5440, -13.6600, 3000, null);

        var first = await Handler(db).Handle(query, CancellationToken.None);
        var second = await Handler(db).Handle(query, CancellationToken.None);

        first.Should().ContainSingle();
        second.Single().Should().BeEquivalentTo(first.Single());
        first.Single().Id.Should().NotContain(driver.Id);
        first.Single().Heading.Should().Be(90);
        // Offset is at most half the fuzz (0.003°) on each axis, and not zero.
        Math.Abs(first.Single().Lat - 9.5440).Should().BeLessThanOrEqualTo(0.0015);
        (first.Single().Lat, first.Single().Lng).Should().NotBe((9.5440, -13.6600));
    }

    [Fact]
    public async Task OnlyDispatchableDriversInsideTheRadius_AreShown()
    {
        using var db = DbContextFactory.Create();
        db.Users.Add(Driver(9.5440m, -13.6600m));
        db.Users.Add(Driver(9.5441m, -13.6601m, KycStatus.Pending));
        db.Users.Add(Driver(9.5735m, -13.6071m)); // ~6.5 km away
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(
            new GetNearbyDriversQuery(9.5440, -13.6600, 3000, null), CancellationToken.None);

        result.Should().ContainSingle();
    }
}
