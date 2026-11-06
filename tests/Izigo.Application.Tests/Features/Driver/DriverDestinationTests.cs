using FluentAssertions;
using Izigo.Application.Common.Helpers;
using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Driver.Commands;
using Izigo.Application.Features.Driver.Dtos;
using Izigo.Application.Features.Driver.Queries;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Moq;
using Xunit;

namespace Izigo.Application.Tests.Features.Driver;

/// <summary>A driver heading home only gets trips that take them home.</summary>
public class DriverDestinationTests
{
    // Abidjan: driver in Plateau, heading to Yopougon (west).
    private const double DriverLat = 5.3236, DriverLng = -4.0205;
    private const double HomeLat = 5.3450, HomeLng = -4.0900;

    [Fact]
    public void A_trip_towards_home_fits_and_one_away_from_it_does_not()
    {
        DriverDestination.Fits(DriverLat, DriverLng, HomeLat, HomeLng,
            5.3240, -4.0220, 5.3420, -4.0820).Should().BeTrue();
        DriverDestination.Fits(DriverLat, DriverLng, HomeLat, HomeLng,
            5.3240, -4.0220, 5.3000, -3.9700).Should().BeFalse();
    }

    private static DriverProfile Driver() => new()
    {
        UserId = "usr_dd", IsOnline = true, KycStatus = KycStatus.Approved, OnboardingComplete = true,
        LastLat = (decimal)DriverLat, LastLng = (decimal)DriverLng, LastLocationAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task The_filter_can_be_switched_on_twice_a_day_and_editing_it_is_free()
    {
        using var db = DbContextFactory.Create();
        db.DriverProfiles.Add(Driver());
        await db.SaveChangesAsync();
        var set = new SetDriverDestinationHandler(db);
        var clear = new ClearDriverDestinationHandler(db);
        var home = new SetDestinationRequest("Home", HomeLat, HomeLng);

        (await set.Handle(new("usr_dd", home), default)).UsesLeftToday.Should().Be(1);
        (await set.Handle(new("usr_dd", home with { Label = "Home, gate 2" }), default)).UsesLeftToday.Should().Be(1);
        await clear.Handle(new("usr_dd"), default);
        (await set.Handle(new("usr_dd", home), default)).UsesLeftToday.Should().Be(0);
        await clear.Handle(new("usr_dd"), default);

        var third = () => set.Handle(new("usr_dd", home), default);
        await third.Should().ThrowAsync<InvalidOperationException>().WithMessage("*2 times a day*");
    }

    [Fact]
    public async Task A_driver_with_a_destination_does_not_claim_a_trip_going_the_other_way()
    {
        using var db = DbContextFactory.Create();
        var dp = Driver();
        dp.DestinationLat = (decimal)HomeLat;
        dp.DestinationLng = (decimal)HomeLng;
        db.DriverProfiles.Add(dp);
        db.Trips.Add(new Trip
        {
            RiderId = "usr_r", JobState = JobState.Broadcasting, Vertical = Vertical.Ride, Market = "ci",
            Currency = "XOF", ServiceClass = ServiceClass.ZenCar,
            PickupLat = 5.3240m, PickupLng = -4.0220m, DropoffLat = 5.3000m, DropoffLng = -3.9700m,
        });
        await db.SaveChangesAsync();

        var offer = await new GetJobOfferHandler(db, new Mock<IJobDispatcher>().Object, FakeServices.DispatchSettings())
            .Handle(new GetJobOfferQuery("usr_dd"), default);

        offer.Should().BeNull();
    }

    [Fact]
    public async Task Reaching_the_destination_switches_the_filter_off()
    {
        using var db = DbContextFactory.Create();
        var dp = Driver();
        dp.DestinationLat = (decimal)HomeLat;
        dp.DestinationLng = (decimal)HomeLng;
        dp.DestinationLabel = "Home";
        db.DriverProfiles.Add(dp);
        await db.SaveChangesAsync();

        await new UpdateLocationHandler(db, FakeServices.Realtime())
            .Handle(new UpdateLocationCommand("usr_dd", new UpdateLocationRequest(HomeLat + 0.001, HomeLng, null, null, null, null)), default);

        db.DriverProfiles.Single().DestinationLat.Should().BeNull();
    }
}
