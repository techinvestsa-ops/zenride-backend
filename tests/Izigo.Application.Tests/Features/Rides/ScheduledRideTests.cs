using FluentAssertions;
using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Driver.Queries;
using Izigo.Application.Features.Rides.Helpers;
using Izigo.Application.Features.Rides.Queries;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Moq;
using Xunit;

namespace Izigo.Application.Tests.Features.Rides;

/// <summary>A ride booked for later stays out of the live system until it is due.</summary>
public class ScheduledRideTests
{
    [Fact]
    public void Pickup_times_too_soon_or_too_far_are_refused()
    {
        var now = DateTime.UtcNow;
        var tooSoon = () => ScheduledRides.Validate(now.AddMinutes(5), now);
        var tooFar  = () => ScheduledRides.Validate(now.AddDays(8), now);

        tooSoon.Should().Throw<ArgumentException>();
        tooFar.Should().Throw<ArgumentException>();
        ScheduledRides.Validate(now.AddHours(2), now).Should().BeCloseTo(now.AddHours(2), TimeSpan.FromSeconds(1));
    }

    private static Trip Booked(DateTime scheduledAt) => new()
    {
        RiderId = "usr_sched", JobState = JobState.Broadcasting, Vertical = Vertical.Ride,
        Market = "gn", Currency = "GNF", ServiceClass = ServiceClass.ZenCar, ScheduledAt = scheduledAt,
        PickupLat = 9.5515m, PickupLng = -13.6723m, DropoffLat = 9.6400m, DropoffLng = -13.6100m,
    };

    [Fact]
    public async Task A_ride_booked_for_tomorrow_is_not_the_riders_active_ride_and_reads_as_scheduled()
    {
        using var db = DbContextFactory.Create();
        db.Trips.Add(Booked(DateTime.UtcNow.AddDays(1)));
        await db.SaveChangesAsync();

        var active = await new GetActiveRideHandler(db, FakeServices.AppSettings(), FakeServices.QuoteSettings())
            .Handle(new GetActiveRideQuery("usr_sched"), CancellationToken.None);
        active.Should().BeNull();

        var detail = await RideDetailMapper.BuildAsync(db.Trips.Single(), db, CancellationToken.None, "", 0);
        detail.Status.Should().Be("scheduled");
    }

    [Fact]
    public async Task A_ride_within_the_lead_time_is_live()
    {
        using var db = DbContextFactory.Create();
        db.Trips.Add(Booked(DateTime.UtcNow + ScheduledRides.Lead - TimeSpan.FromMinutes(1)));
        await db.SaveChangesAsync();

        var active = await new GetActiveRideHandler(db, FakeServices.AppSettings(), FakeServices.QuoteSettings())
            .Handle(new GetActiveRideQuery("usr_sched"), CancellationToken.None);
        active.Should().NotBeNull();
        active!.Status.Should().Be("searching");
    }

    [Fact]
    public async Task Drivers_cannot_claim_a_ride_that_is_not_due()
    {
        using var db = DbContextFactory.Create();
        db.DriverProfiles.Add(new DriverProfile
        {
            UserId = "usr_driver", IsOnline = true, KycStatus = KycStatus.Approved, OnboardingComplete = true,
            LastLat = 9.5440m, LastLng = -13.6600m, LastLocationAt = DateTime.UtcNow,
        });
        db.Trips.Add(Booked(DateTime.UtcNow.AddHours(3)));
        await db.SaveChangesAsync();

        var offer = await new GetJobOfferHandler(db, new Mock<IJobDispatcher>().Object, FakeServices.DispatchSettings())
            .Handle(new GetJobOfferQuery("usr_driver"), CancellationToken.None);

        offer.Should().BeNull();
        db.Trips.Single().JobState.Should().Be(JobState.Broadcasting);
    }
}
