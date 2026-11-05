using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Packages.Commands;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Izigo.Infrastructure.Jobs.Processors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using DomainBackgroundJob = Izigo.Domain.Entities.BackgroundJob;

namespace Izigo.Application.Tests.Features.Packages;

/// <summary>
/// A package search must end: when no courier is found within the search
/// window the package is closed so the sender's app can stop waiting, and
/// the sender can start the same search again.
/// </summary>
public class PackageSearchTimeoutTests
{
    private static async Task<(global::Izigo.Infrastructure.Persistence.ApplicationDbContext Db, Package Pkg, Trip Trip)>
        Setup(TimeSpan searchAge, JobState state = JobState.Broadcasting, PackageStatus status = PackageStatus.Searching)
    {
        var db = DbContextFactory.Create();
        var quoteId = Guid.NewGuid().ToString();
        var pkg = new Package
        {
            TrackingId = "ZD-0001-0001", SenderId = "usr_sender", QuoteId = quoteId,
            PickupLat = 9.5735m, PickupLng = -13.6071m, PickupLabel = "Plage",
            DropoffLat = 9.5755m, DropoffLng = -13.6199m, DropoffLabel = "Airport",
            RecipientName = "John", RecipientPhone = "+224620000000", Size = "large",
            Status = status, FareTotal = 8000, Currency = "GNF", PaymentMethod = PaymentMethod.Cash, Market = "gn",
        };
        var trip = new Trip
        {
            RiderId = "usr_sender", QuoteId = quoteId, JobState = state, Vertical = Vertical.Package,
            Market = "gn", Currency = "GNF", ServiceClass = ServiceClass.PackageLarge,
            PickupLat = pkg.PickupLat, PickupLng = pkg.PickupLng, DropoffLat = pkg.DropoffLat, DropoffLng = pkg.DropoffLng,
        };
        db.Packages.Add(pkg);
        db.Trips.Add(trip);
        db.TripStateHistories.Add(new TripStateHistory
        {
            TripId = trip.Id, State = JobState.Broadcasting, OccurredAt = DateTime.UtcNow - searchAge, Actor = "system",
        });
        await db.SaveChangesAsync();
        return (db, pkg, trip);
    }

    private static (DispatchJobProcessor Processor, Mock<IBackgroundJobClient> Client) Processor(
        global::Izigo.Infrastructure.Persistence.ApplicationDbContext db)
    {
        var client = new Mock<IBackgroundJobClient>();
        client.Setup(c => c.Create(It.IsAny<Job>(), It.IsAny<IState>())).Returns("hf");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DispatchDefaults:BatchWindowSeconds"] = "0" })
            .Build();
        var processor = new DispatchJobProcessor(db, FakeServices.Realtime(), FakeServices.Push(), FakeServices.Geo(),
            client.Object, config, NullLogger<DispatchJobProcessor>.Instance);
        return (processor, client);
    }

    private static async Task<string> QueueDispatch(
        global::Izigo.Infrastructure.Persistence.ApplicationDbContext db, Trip trip)
    {
        var job = new DomainBackgroundJob { Type = $"dispatch:{trip.Id}", Status = "queued" };
        db.BackgroundJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    [Fact]
    public async Task NoCourier_WithinTheSearchWindow_KeepsSearching()
    {
        var (db, pkg, trip) = await Setup(TimeSpan.FromSeconds(30));
        using var _ = db;
        var (processor, client) = Processor(db);

        await processor.ProcessAsync(await QueueDispatch(db, trip), CancellationToken.None);

        trip.JobState.Should().Be(JobState.Broadcasting);
        pkg.Status.Should().Be(PackageStatus.Searching);
        client.Verify(c => c.Create(It.IsAny<Job>(), It.IsAny<ScheduledState>()), Times.Once);
    }

    [Fact]
    public async Task NoCourier_AfterTheSearchWindow_ClosesThePackage()
    {
        var (db, pkg, trip) = await Setup(TimeSpan.FromMinutes(4));
        using var _ = db;
        var (processor, _) = Processor(db);

        await processor.ProcessAsync(await QueueDispatch(db, trip), CancellationToken.None);

        trip.JobState.Should().Be(JobState.Expired);
        pkg.Status.Should().Be(PackageStatus.Cancelled);
        pkg.CancelledAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Retry_AfterTheSearchRanOut_SearchesAgain()
    {
        var (db, pkg, trip) = await Setup(TimeSpan.FromMinutes(4), JobState.Expired, PackageStatus.Cancelled);
        using var _ = db;
        var jobs = new Mock<IJobDispatcher>();

        var dto = await new RetryPackageHandler(db, FakeServices.Realtime(), jobs.Object)
            .Handle(new RetryPackageCommand("usr_sender", pkg.Id), CancellationToken.None);

        dto.Status.Should().Be("searching");
        trip.JobState.Should().Be(JobState.Broadcasting);
        pkg.CancelledAt.Should().BeNull();
        jobs.Verify(j => j.Enqueue(It.IsAny<string>(), $"dispatch:{trip.Id}"), Times.Once);
        (await db.TripStateHistories.CountAsync(h => h.TripId == trip.Id && h.State == JobState.Broadcasting))
            .Should().Be(2);
    }

    [Fact]
    public async Task Retry_APackageTheSenderCancelled_IsRefused()
    {
        var (db, pkg, _) = await Setup(TimeSpan.FromMinutes(1), JobState.CancelledByRider, PackageStatus.Cancelled);
        using var __ = db;

        var act = () => new RetryPackageHandler(db, FakeServices.Realtime(), FakeServices.JobDispatcher())
            .Handle(new RetryPackageCommand("usr_sender", pkg.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("CONFLICT:*");
    }
}
