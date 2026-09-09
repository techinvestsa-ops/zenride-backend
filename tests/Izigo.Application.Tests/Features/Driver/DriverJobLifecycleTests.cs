using FluentAssertions;
using Izigo.Application.Features.Driver.Commands;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Enums;
using Xunit;
using UserEntity = Izigo.Domain.Entities.User;
using TripEntity = Izigo.Domain.Entities.Trip;
using DriverProfileEntity = Izigo.Domain.Entities.DriverProfile;
using DriverWalletEntity = Izigo.Domain.Entities.DriverWallet;
using WalletEntity = Izigo.Domain.Entities.Wallet;

namespace Izigo.Application.Tests.Features.Driver;

public class DriverJobLifecycleTests
{
    private static TripEntity BuildTrip(string driverId, string riderId,
        JobState state = JobState.Offered) => new()
    {
        Code          = "ZR-0001",
        Vertical      = Vertical.Ride,
        ServiceClass  = ServiceClass.ZenCar,
        RiderId       = riderId,
        DriverId      = driverId,
        Market        = "ci",
        Currency      = "XOF",
        JobState      = state,
        PickupLat     = 5.3m, PickupLng  = -4.0m, PickupLabel  = "Pickup",
        DropoffLat    = 5.4m, DropoffLng = -4.1m, DropoffLabel = "Dropoff",
        PaymentMethod = PaymentMethod.Cash,
        FareGross     = 1200, FareBase = 500, FareDistance = 600, FareTime = 100,
        FareServiceFee = 96, StartOtp = "1234"
    };

    private static UserEntity BuildDriverUser() => new()
    {
        FirstName = "Driver", LastName = "Test",
        Phone = "+2250700000001", Role = UserRole.Driver, Rating = 5.0m
    };

    private static UserEntity BuildRiderUser() => new()
    {
        FirstName = "Rider", LastName = "Test",
        Phone = "+2250700000002", Role = UserRole.Rider,
        Rating = 4.0m, TotalRatings = 10
    };

    // ── AcceptJobHandler ──────────────────────────────────────────────────────

    [Fact]
    public async Task AcceptJob_NotFound_ThrowsKeyNotFound()
    {
        using var db = DbContextFactory.Create();
        var handler = new AcceptJobHandler(db, FakeServices.Realtime(), FakeServices.Push());

        var act = () => handler.Handle(
            new AcceptJobCommand("driver1", "nonexistent"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task AcceptJob_WrongState_ThrowsConflict()
    {
        using var db = DbContextFactory.Create();
        var driverId = Guid.NewGuid().ToString();
        var riderId  = Guid.NewGuid().ToString();
        var trip = BuildTrip(driverId, riderId, JobState.Accepted);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var handler = new AcceptJobHandler(db, FakeServices.Realtime(), FakeServices.Push());
        var act = () => handler.Handle(
            new AcceptJobCommand(driverId, trip.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Offered*");
    }

    [Fact]
    public async Task AcceptJob_OfferedState_TransitionsToAccepted()
    {
        using var db = DbContextFactory.Create();
        var driver = BuildDriverUser();
        db.Users.Add(driver);
        var riderId = Guid.NewGuid().ToString();
        var trip = BuildTrip(driver.Id, riderId, JobState.Offered);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var handler = new AcceptJobHandler(db, FakeServices.Realtime(), FakeServices.Push());
        var result  = await handler.Handle(
            new AcceptJobCommand(driver.Id, trip.Id), CancellationToken.None);

        result.Should().NotBeNull();
        db.Trips.Find(trip.Id)!.JobState.Should().Be(JobState.Accepted);
        db.TripStateHistories.Should().HaveCountGreaterThan(0);
    }

    // ── DeclineJobHandler ─────────────────────────────────────────────────────

    [Fact]
    public async Task DeclineJob_NotFound_ThrowsKeyNotFound()
    {
        using var db = DbContextFactory.Create();
        var handler = new DeclineJobHandler(db, FakeServices.JobDispatcher());

        var act = () => handler.Handle(
            new DeclineJobCommand("driver1", "nonexistent", null), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task DeclineJob_Offered_ResetsTobroadcasting_AndEnqueuesDispatch()
    {
        using var db = DbContextFactory.Create();
        var driverId = Guid.NewGuid().ToString();
        var riderId  = Guid.NewGuid().ToString();
        var dp = new DriverProfileEntity { UserId = driverId, AcceptanceRate = 0.9m };
        db.DriverProfiles.Add(dp);
        var trip = BuildTrip(driverId, riderId, JobState.Offered);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var handler = new DeclineJobHandler(db, FakeServices.JobDispatcher());
        await handler.Handle(
            new DeclineJobCommand(driverId, trip.Id, "Too far"), CancellationToken.None);

        var updated = db.Trips.Find(trip.Id)!;
        updated.JobState.Should().Be(JobState.Broadcasting);
        updated.DriverId.Should().BeNull();
        db.DriverProfiles.First(d => d.UserId == driverId).AcceptanceRate.Should().BeLessThan(0.9m);
        db.BackgroundJobs.Should().HaveCount(1);
    }

    // ── EnRouteHandler ────────────────────────────────────────────────────────

    [Fact]
    public async Task EnRoute_FromAccepted_TransitionsToEnRouteToPickup()
    {
        using var db = DbContextFactory.Create();
        var driver = BuildDriverUser();
        db.Users.Add(driver);
        var riderId = Guid.NewGuid().ToString();
        var trip = BuildTrip(driver.Id, riderId, JobState.Accepted);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var handler = new EnRouteHandler(db, FakeServices.Realtime(), FakeServices.Push());
        await handler.Handle(new EnRouteCommand(driver.Id, trip.Id), CancellationToken.None);

        db.Trips.Find(trip.Id)!.JobState.Should().Be(JobState.EnRouteToPickup);
    }

    [Fact]
    public async Task EnRoute_FromPickedUp_TransitionsToEnRouteToDropoff()
    {
        using var db = DbContextFactory.Create();
        var driverId = Guid.NewGuid().ToString();
        var riderId  = Guid.NewGuid().ToString();
        var trip = BuildTrip(driverId, riderId, JobState.PickedUp);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var handler = new EnRouteHandler(db, FakeServices.Realtime(), FakeServices.Push());
        await handler.Handle(new EnRouteCommand(driverId, trip.Id), CancellationToken.None);

        db.Trips.Find(trip.Id)!.JobState.Should().Be(JobState.EnRouteToDropoff);
    }

    [Fact]
    public async Task EnRoute_FromInvalidState_ThrowsConflict()
    {
        using var db = DbContextFactory.Create();
        var driverId = Guid.NewGuid().ToString();
        var riderId  = Guid.NewGuid().ToString();
        var trip = BuildTrip(driverId, riderId, JobState.Broadcasting);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var handler = new EnRouteHandler(db, FakeServices.Realtime(), FakeServices.Push());
        var act = () => handler.Handle(
            new EnRouteCommand(driverId, trip.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*CONFLICT*");
    }

    // ── ArrivedPickupHandler ──────────────────────────────────────────────────

    [Theory]
    [InlineData(JobState.Accepted)]
    [InlineData(JobState.EnRouteToPickup)]
    public async Task ArrivedPickup_ValidStates_TransitionsToArrivedAtPickup(JobState fromState)
    {
        using var db = DbContextFactory.Create();
        var driver = BuildDriverUser();
        db.Users.Add(driver);
        var riderId = Guid.NewGuid().ToString();
        var trip = BuildTrip(driver.Id, riderId, fromState);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var handler = new ArrivedPickupHandler(db, FakeServices.Realtime(), FakeServices.Push());
        await handler.Handle(new ArrivedPickupCommand(driver.Id, trip.Id), CancellationToken.None);

        db.Trips.Find(trip.Id)!.JobState.Should().Be(JobState.ArrivedAtPickup);
        db.Trips.Find(trip.Id)!.ArrivedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ArrivedPickup_WrongState_ThrowsConflict()
    {
        using var db = DbContextFactory.Create();
        var driverId = Guid.NewGuid().ToString();
        var riderId  = Guid.NewGuid().ToString();
        var trip = BuildTrip(driverId, riderId, JobState.Broadcasting);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var handler = new ArrivedPickupHandler(db, FakeServices.Realtime(), FakeServices.Push());
        var act = () => handler.Handle(
            new ArrivedPickupCommand(driverId, trip.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Cannot mark arrived*");
    }

    // ── StartJobHandler ───────────────────────────────────────────────────────

    [Fact]
    public async Task StartJob_CorrectOtp_TransitionsToPickedUp()
    {
        using var db = DbContextFactory.Create();
        var driverId = Guid.NewGuid().ToString();
        var riderId  = Guid.NewGuid().ToString();
        var trip = BuildTrip(driverId, riderId, JobState.ArrivedAtPickup);
        trip.StartOtp = "9999";
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var handler = new StartJobHandler(db, FakeServices.Realtime(), FakeServices.Push());
        await handler.Handle(
            new StartJobCommand(driverId, trip.Id, "9999"), CancellationToken.None);

        db.Trips.Find(trip.Id)!.JobState.Should().Be(JobState.PickedUp);
    }

    [Fact]
    public async Task StartJob_WrongOtp_ThrowsUnauthorized()
    {
        using var db = DbContextFactory.Create();
        var driverId = Guid.NewGuid().ToString();
        var riderId  = Guid.NewGuid().ToString();
        var trip = BuildTrip(driverId, riderId, JobState.ArrivedAtPickup);
        trip.StartOtp = "9999";
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var handler = new StartJobHandler(db, FakeServices.Realtime(), FakeServices.Push());
        var act = () => handler.Handle(
            new StartJobCommand(driverId, trip.Id, "0000"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task StartJob_WrongState_ThrowsConflict()
    {
        using var db = DbContextFactory.Create();
        var driverId = Guid.NewGuid().ToString();
        var riderId  = Guid.NewGuid().ToString();
        var trip = BuildTrip(driverId, riderId, JobState.Accepted);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var handler = new StartJobHandler(db, FakeServices.Realtime(), FakeServices.Push());
        var act = () => handler.Handle(
            new StartJobCommand(driverId, trip.Id, "1234"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Cannot start*");
    }

    // ── ArrivedDropoffHandler ─────────────────────────────────────────────────

    [Theory]
    [InlineData(JobState.PickedUp)]
    [InlineData(JobState.EnRouteToDropoff)]
    public async Task ArrivedDropoff_ValidStates_TransitionsToArrivedAtDropoff(JobState fromState)
    {
        using var db = DbContextFactory.Create();
        var driverId = Guid.NewGuid().ToString();
        var riderId  = Guid.NewGuid().ToString();
        var trip = BuildTrip(driverId, riderId, fromState);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var handler = new ArrivedDropoffHandler(db);
        await handler.Handle(new ArrivedDropoffCommand(driverId, trip.Id), CancellationToken.None);

        db.Trips.Find(trip.Id)!.JobState.Should().Be(JobState.ArrivedAtDropoff);
    }

    // ── CompleteJobHandler ────────────────────────────────────────────────────

    [Fact]
    public async Task CompleteJob_CashPayment_SetsCommissionAndCashCollected()
    {
        using var db = DbContextFactory.Create();
        var driverId = Guid.NewGuid().ToString();
        var riderId  = Guid.NewGuid().ToString();
        var dp = new DriverProfileEntity { UserId = driverId };
        var dw = new DriverWalletEntity { DriverId = driverId, AvailableBalance = 0 };
        dp.DriverWallet = dw;
        db.DriverProfiles.Add(dp);

        var trip = BuildTrip(driverId, riderId, JobState.ArrivedAtDropoff);
        trip.PaymentMethod = PaymentMethod.Cash;
        trip.FareGross = 2000; trip.FareServiceFee = 160; trip.FareDiscount = 0; trip.FareTip = 0;
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var handler = new CompleteJobHandler(db, FakeServices.Realtime(), FakeServices.Push());
        await handler.Handle(new CompleteJobCommand(driverId, trip.Id), CancellationToken.None);

        var updated = db.Trips.Find(trip.Id)!;
        updated.JobState.Should().Be(JobState.Completed);
        updated.FareIsFinal.Should().BeTrue();
        updated.CashCollected.Should().BeGreaterThan(0);
        updated.CommissionAmount.Should().BeGreaterThan(0);
        updated.DriverEarnings.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CompleteJob_WalletPayment_DebitsRiderWallet()
    {
        using var db = DbContextFactory.Create();
        var driverId = Guid.NewGuid().ToString();
        var riderId  = Guid.NewGuid().ToString();
        var dp = new DriverProfileEntity { UserId = driverId };
        var dw = new DriverWalletEntity { DriverId = driverId, AvailableBalance = 0 };
        dp.DriverWallet = dw;
        db.DriverProfiles.Add(dp);

        var riderWallet = new WalletEntity
        {
            UserId = riderId, Balance = 10000, Currency = "XOF",
            MinTopup = 500, MaxBalance = 500_000
        };
        db.Wallets.Add(riderWallet);

        var trip = BuildTrip(driverId, riderId, JobState.ArrivedAtDropoff);
        trip.PaymentMethod = PaymentMethod.Wallet;
        trip.FareGross = 2000; trip.FareServiceFee = 160; trip.FareDiscount = 0; trip.FareTip = 0;
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var handler = new CompleteJobHandler(db, FakeServices.Realtime(), FakeServices.Push());
        await handler.Handle(new CompleteJobCommand(driverId, trip.Id), CancellationToken.None);

        db.Trips.Find(trip.Id)!.JobState.Should().Be(JobState.Completed);
        db.Wallets.First(w => w.UserId == riderId).Balance.Should().BeLessThan(10000);
        db.WalletTransactions.Should().HaveCount(1);
    }

    [Fact]
    public async Task CompleteJob_WrongState_ThrowsConflict()
    {
        using var db = DbContextFactory.Create();
        var driverId = Guid.NewGuid().ToString();
        var riderId  = Guid.NewGuid().ToString();
        var trip = BuildTrip(driverId, riderId, JobState.PickedUp);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var handler = new CompleteJobHandler(db, FakeServices.Realtime(), FakeServices.Push());
        var act = () => handler.Handle(
            new CompleteJobCommand(driverId, trip.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Cannot complete*");
    }

    // ── CancelJobHandler ──────────────────────────────────────────────────────

    [Fact]
    public async Task CancelJob_ActiveState_SetsCancelledByDriver()
    {
        using var db = DbContextFactory.Create();
        var driverId = Guid.NewGuid().ToString();
        var riderId  = Guid.NewGuid().ToString();
        var dp = new DriverProfileEntity { UserId = driverId, CancellationRate = 0.0m };
        db.DriverProfiles.Add(dp);
        var trip = BuildTrip(driverId, riderId, JobState.Accepted);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var handler = new CancelJobHandler(db, FakeServices.Realtime(), FakeServices.Push());
        await handler.Handle(
            new CancelJobCommand(driverId, trip.Id, "vehicle_issue"), CancellationToken.None);

        var updated = db.Trips.Find(trip.Id)!;
        updated.JobState.Should().Be(JobState.CancelledByDriver);
        updated.CancellationReasonCode.Should().Be("vehicle_issue");
        db.DriverProfiles.First(d => d.UserId == driverId).CancellationRate.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CancelJob_TerminalState_ThrowsConflict()
    {
        using var db = DbContextFactory.Create();
        var driverId = Guid.NewGuid().ToString();
        var riderId  = Guid.NewGuid().ToString();
        var trip = BuildTrip(driverId, riderId, JobState.Completed);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var handler = new CancelJobHandler(db, FakeServices.Realtime(), FakeServices.Push());
        var act = () => handler.Handle(
            new CancelJobCommand(driverId, trip.Id, null), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*terminal state*");
    }

    // ── RateCustomerHandler ───────────────────────────────────────────────────

    [Fact]
    public async Task RateCustomer_InvalidStars_ThrowsArgumentException()
    {
        using var db = DbContextFactory.Create();
        var handler = new RateCustomerHandler(db);

        var act = () => handler.Handle(
            new RateCustomerCommand("driver1", "trip1", 6, null), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*1–5*");
    }

    [Fact]
    public async Task RateCustomer_AlreadyRated_ThrowsConflict()
    {
        using var db = DbContextFactory.Create();
        var driverId = Guid.NewGuid().ToString();
        var riderId  = Guid.NewGuid().ToString();
        var trip = BuildTrip(driverId, riderId, JobState.Completed);
        trip.RatingByDriver = 4;
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var handler = new RateCustomerHandler(db);
        var act = () => handler.Handle(
            new RateCustomerCommand(driverId, trip.Id, 5, null), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already rated*");
    }

    [Fact]
    public async Task RateCustomer_Completed_UpdatesRiderRating()
    {
        using var db = DbContextFactory.Create();
        var driverId = Guid.NewGuid().ToString();
        var rider    = BuildRiderUser();
        db.Users.Add(rider);
        var trip = BuildTrip(driverId, rider.Id, JobState.Completed);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var handler = new RateCustomerHandler(db);
        await handler.Handle(
            new RateCustomerCommand(driverId, trip.Id, 5, "Great rider!"), CancellationToken.None);

        db.Trips.Find(trip.Id)!.RatingByDriver.Should().Be(5);
        db.Users.Find(rider.Id)!.TotalRatings.Should().Be(11);
    }
}
