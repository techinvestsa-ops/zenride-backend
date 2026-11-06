using FluentAssertions;
using Izigo.Application.Features.Geo.Queries;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Xunit;

namespace Izigo.Application.Tests.Features.Geo;

/// <summary>Suggested pickup spots come from where rides were really picked up.</summary>
public class PickupSpotsTests
{
    private static Trip Picked(decimal lat, decimal lng, string label) => new()
    {
        RiderId = "usr_r", JobState = JobState.Completed, Vertical = Vertical.Ride, Market = "ci",
        Currency = "XOF", ServiceClass = ServiceClass.ZenCar, PickupLat = lat, PickupLng = lng, PickupLabel = label,
    };

    [Fact]
    public async Task A_spot_used_by_several_rides_is_suggested_and_a_one_off_is_not()
    {
        using var db = DbContextFactory.Create();
        for (var i = 0; i < 4; i++) db.Trips.Add(Picked(5.32370m, -4.02060m, "Pharmacie du Plateau"));
        db.Trips.Add(Picked(5.32450m, -4.01980m, "Somewhere once"));
        await db.SaveChangesAsync();

        var spots = await new GetPickupSpotsHandler(db).Handle(new GetPickupSpotsQuery(5.3236, -4.0205), default);

        spots.Should().ContainSingle();
        spots[0].Label.Should().Be("Pharmacie du Plateau");
        spots[0].Pickups.Should().Be(4);
        spots[0].DistanceM.Should().BeLessThan(50);
    }
}
