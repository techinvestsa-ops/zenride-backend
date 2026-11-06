using FluentAssertions;
using Hangfire;
using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Driver.Commands;
using Izigo.Application.Features.Quotes.Commands;
using Izigo.Application.Features.Quotes.Dtos;
using Izigo.Application.Features.Rides.Commands;
using Izigo.Application.Features.Rides.Dtos;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Izigo.Infrastructure.Jobs.Processors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using DomainBackgroundJob = Izigo.Domain.Entities.BackgroundJob;

namespace Izigo.Application.Tests.Features.Rides;

/// <summary>
/// The main ride path end to end through the real handlers: quote, book,
/// dispatch, accept, drive to pickup, arrive, start with the PIN, arrive at
/// the drop-off, complete, rate. Paid by wallet so the money is checked too.
/// </summary>
public class MainRideFlowTests
{
    [Fact]
    public async Task A_ride_goes_from_request_to_rating_and_the_money_lands()
    {
        using var db = DbContextFactory.Create();
        var rider = new User { Phone = "+2250700000201", Role = UserRole.Rider, Status = UserStatus.Active };
        var driverUser = new User { Phone = "+2250700000202", Role = UserRole.Driver, Status = UserStatus.Active };
        db.Users.AddRange(rider, driverUser);
        db.Wallets.Add(new Izigo.Domain.Entities.Wallet { UserId = rider.Id, Balance = 100_000 });
        db.DriverWallets.Add(new DriverWallet { DriverId = driverUser.Id, Currency = "XOF" });
        db.DriverProfiles.Add(new DriverProfile
        {
            UserId = driverUser.Id, User = driverUser, IsOnline = true, KycStatus = KycStatus.Approved,
            OnboardingComplete = true, VerticalsAllowed = [Vertical.Ride],
            LastLat = 5.3230m, LastLng = -4.0200m, LastLocationAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        var (rt, push) = (FakeServices.Realtime(), FakeServices.Push());

        // 1. Rider: quote and book, paying by wallet.
        var quote = await new CreateQuoteHandler(db, FakeServices.Geo(), FakeServices.QuoteSettings())
            .Handle(new CreateQuoteCommand(rider.Id, new CreateQuoteRequest("ride",
                new QuoteLocationInput("Plateau", 5.3236, -4.0205, null),
                new QuoteLocationInput("Cocody", 5.3600, -3.9900, null), null, null, null, null)), default);
        var idempotency = new Mock<IIdempotencyService>();
        var ride = await new CreateRideHandler(db, idempotency.Object, FakeServices.AppSettings(),
                FakeServices.QuoteSettings(), new Mock<IJobDispatcher>().Object, rt)
            .Handle(new CreateRideCommand(rider.Id,
                new CreateRideRequest(quote.QuoteId, quote.Options[0].ClassCode, "wallet", null, null, null, null), null), default);
        ride.Status.Should().Be("searching");

        // 2. Dispatch offers it to the nearby driver.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DispatchDefaults:BatchWindowSeconds"] = "0" }).Build();
        var job = new DomainBackgroundJob { Type = $"dispatch:{ride.TripId}", Status = "queued" };
        db.BackgroundJobs.Add(job);
        await db.SaveChangesAsync();
        await new DispatchJobProcessor(db, rt, push, FakeServices.Geo(), new Mock<IBackgroundJobClient>().Object,
            config, NullLogger<DispatchJobProcessor>.Instance).ProcessAsync(job.Id, default);
        var trip = db.Trips.Single(t => t.Id == ride.TripId);
        trip.JobState.Should().Be(JobState.Offered);
        trip.DriverId.Should().Be(driverUser.Id);

        // 3. Driver: accept, head to pickup, arrive, start with the rider's PIN.
        var d = driverUser.Id;
        await new AcceptJobHandler(db, rt, push).Handle(new AcceptJobCommand(d, trip.Id), default);
        await new EnRouteHandler(db, rt, push).Handle(new EnRouteCommand(d, trip.Id), default);
        await new ArrivedPickupHandler(db, rt, push).Handle(new ArrivedPickupCommand(d, trip.Id), default);
        var wrongPin = () => new StartJobHandler(db, rt, push).Handle(new StartJobCommand(d, trip.Id, "0000" == trip.StartOtp ? "1111" : "0000"), default);
        await wrongPin.Should().ThrowAsync<Exception>();
        await new StartJobHandler(db, rt, push).Handle(new StartJobCommand(d, trip.Id, trip.StartOtp!), default);

        // 4. Drive, arrive at the drop-off, complete.
        await new EnRouteHandler(db, rt, push).Handle(new EnRouteCommand(d, trip.Id), default);
        await new ArrivedDropoffHandler(db, rt, push).Handle(new ArrivedDropoffCommand(d, trip.Id), default);
        await new CompleteJobHandler(db, rt, push).Handle(new CompleteJobCommand(d, trip.Id), default);
        trip.JobState.Should().Be(JobState.Completed);

        // 5. Rider rates.
        await new RateRideHandler(db).Handle(new RateRideCommand(rider.Id, trip.Id, 5, null, "Great", null), default);
        trip.RatingByRider.Should().Be(5);

        // Money: the rider paid the fare from the wallet; the driver got their share.
        var paid = 100_000 - db.Wallets.Single(w => w.UserId == rider.Id).Balance;
        paid.Should().BeGreaterThan(0);
        db.DriverWallets.Single().AvailableBalance.Should().BeGreaterThan(0).And.BeLessThan(paid);
    }
}
