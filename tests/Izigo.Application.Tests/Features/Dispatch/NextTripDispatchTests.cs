using FluentAssertions;
using Hangfire;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Izigo.Infrastructure.Jobs.Processors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using DomainBackgroundJob = Izigo.Domain.Entities.BackgroundJob;

namespace Izigo.Application.Tests.Features.Dispatch;

/// <summary>A driver about to drop off can be offered the next ride from that drop-off.</summary>
public class NextTripDispatchTests
{
    // Current trip ends at Plateau; the next rider waits 600 m from there.
    private const decimal DropLat = 5.3236m, DropLng = -4.0205m;

    private static async Task<(Izigo.Infrastructure.Persistence.ApplicationDbContext db, Trip next, string driverId)>
        Setup(decimal driverLatOffset)
    {
        var db = DbContextFactory.Create();
        var user = new User { Phone = "+2250700001111", Role = UserRole.Driver, Status = UserStatus.Active };
        db.Users.Add(user);
        db.DriverProfiles.Add(new DriverProfile
        {
            UserId = user.Id, User = user, IsOnline = true, KycStatus = KycStatus.Approved, OnboardingComplete = true,
            VerticalsAllowed = [Vertical.Ride],
            LastLat = DropLat + driverLatOffset, LastLng = DropLng, LastLocationAt = DateTime.UtcNow,
        });
        db.Trips.Add(new Trip
        {
            RiderId = "usr_now", DriverId = user.Id, JobState = JobState.EnRouteToDropoff, Vertical = Vertical.Ride,
            Market = "ci", Currency = "XOF", ServiceClass = ServiceClass.ZenCar,
            PickupLat = 5.30m, PickupLng = -4.05m, DropoffLat = DropLat, DropoffLng = DropLng,
        });
        var next = new Trip
        {
            RiderId = "usr_next", JobState = JobState.Broadcasting, Vertical = Vertical.Ride,
            Market = "ci", Currency = "XOF", ServiceClass = ServiceClass.ZenCar,
            PickupLat = DropLat + 0.0054m, PickupLng = DropLng, DropoffLat = 5.36m, DropoffLng = -4.00m,
        };
        db.Trips.Add(next);
        await db.SaveChangesAsync();
        return (db, next, user.Id);
    }

    private static async Task Dispatch(Izigo.Infrastructure.Persistence.ApplicationDbContext db, Trip trip)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DispatchDefaults:BatchWindowSeconds"] = "0" })
            .Build();
        var processor = new DispatchJobProcessor(db, FakeServices.Realtime(), FakeServices.Push(), FakeServices.Geo(),
            new Mock<IBackgroundJobClient>().Object, config, NullLogger<DispatchJobProcessor>.Instance);
        var job = new DomainBackgroundJob { Type = $"dispatch:{trip.Id}", Status = "queued" };
        db.BackgroundJobs.Add(job);
        await db.SaveChangesAsync();
        await processor.ProcessAsync(job.Id, CancellationToken.None);
    }

    [Fact]
    public async Task A_driver_400m_from_their_drop_off_is_offered_the_next_ride()
    {
        var (db, next, driverId) = await Setup(driverLatOffset: -0.0036m);
        using var _ = db;

        await Dispatch(db, next);

        next.JobState.Should().Be(JobState.Offered);
        next.DriverId.Should().Be(driverId);
    }

    [Fact]
    public async Task A_driver_still_5km_from_their_drop_off_is_not()
    {
        var (db, next, _) = await Setup(driverLatOffset: -0.045m);
        using var __ = db;

        await Dispatch(db, next);

        next.DriverId.Should().BeNull();
    }
}
