using FluentAssertions;
using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Driver.Queries;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Moq;
using Xunit;

namespace Izigo.Application.Tests.Features.Driver;

public class JobOfferClaimTests
{
    private static async Task<(global::Izigo.Infrastructure.Persistence.ApplicationDbContext Db, Trip Trip)> Setup(
        int? marketTimeoutSec = null)
    {
        var db = DbContextFactory.Create();
        db.DriverProfiles.Add(new DriverProfile
        {
            UserId             = "usr_driver",
            IsOnline           = true,
            KycStatus          = KycStatus.Approved,
            OnboardingComplete = true,
            LastLat            = 9.5440m,
            LastLng            = -13.6600m,
            LastLocationAt     = DateTime.UtcNow,
        });
        var trip = new Trip
        {
            RiderId      = "usr_rider",
            JobState     = JobState.Broadcasting,
            Vertical     = Vertical.Ride,
            Market       = "gn",
            Currency     = "GNF",
            ServiceClass = ServiceClass.ZenCar,
            PickupLat    = 9.5515m,
            PickupLng    = -13.6723m,
            DropoffLat   = 9.6400m,
            DropoffLng   = -13.6100m,
        };
        db.Trips.Add(trip);
        if (marketTimeoutSec is int t)
            db.DispatchConfigs.Add(new DispatchConfig { Market = "gn", OfferTimeoutSeconds = t });
        await db.SaveChangesAsync();
        return (db, trip);
    }

    [Fact]
    public async Task ClaimingANearbyTrip_SchedulesTheOfferToExpire_AfterTheMarketTimeout()
    {
        var (db, trip) = await Setup(marketTimeoutSec: 45);
        using var _ = db;
        var jobs = new Mock<IJobDispatcher>();

        var offer = await new GetJobOfferHandler(db, jobs.Object, FakeServices.DispatchSettings())
            .Handle(new GetJobOfferQuery("usr_driver"), CancellationToken.None);

        offer.Should().NotBeNull();
        trip.JobState.Should().Be(JobState.Offered);
        jobs.Verify(j => j.Schedule(trip.Id, "offer_expire",
            It.Is<DateTime>(at => at > DateTime.UtcNow.AddSeconds(40) && at < DateTime.UtcNow.AddSeconds(50))),
            Times.Once);
    }

    [Fact]
    public async Task ClaimingANearbyTrip_FallsBackToTheDefaultTimeout()
    {
        var (db, trip) = await Setup();
        using var _ = db;
        var jobs = new Mock<IJobDispatcher>();

        await new GetJobOfferHandler(db, jobs.Object, FakeServices.DispatchSettings(s => s.OfferTimeoutSeconds = 60))
            .Handle(new GetJobOfferQuery("usr_driver"), CancellationToken.None);

        jobs.Verify(j => j.Schedule(trip.Id, "offer_expire",
            It.Is<DateTime>(at => at > DateTime.UtcNow.AddSeconds(55))), Times.Once);
    }

    [Fact]
    public async Task AnOfferAlreadyHeld_IsReturnedAgain_WithoutSchedulingAnotherExpiry()
    {
        var (db, trip) = await Setup();
        using var _ = db;
        trip.JobState = JobState.Offered;
        trip.DriverId = "usr_driver";
        await db.SaveChangesAsync();
        var jobs = new Mock<IJobDispatcher>();

        var offer = await new GetJobOfferHandler(db, jobs.Object, FakeServices.DispatchSettings())
            .Handle(new GetJobOfferQuery("usr_driver"), CancellationToken.None);

        offer.Should().NotBeNull();
        jobs.Verify(j => j.Schedule(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
    }
}
