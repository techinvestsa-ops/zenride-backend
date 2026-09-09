using FluentAssertions;
using Izigo.Application.Features.Rides.Commands;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Xunit;

namespace Izigo.Application.Tests.Features.Rides;

public class RideCancelRateTests
{
    private static Trip BuildTrip(string riderId, JobState state = JobState.Broadcasting) => new()
    {
        Code        = "ZR-0001",
        Vertical    = Vertical.Ride,
        ServiceClass = ServiceClass.ZenCar,
        RiderId     = riderId,
        Market      = "ci",
        Currency    = "XOF",
        JobState    = state,
        PickupLat   = 5.3m, PickupLng = -4.0m, PickupLabel = "A",
        DropoffLat  = 5.4m, DropoffLng = -4.1m, DropoffLabel = "B",
        PaymentMethod = PaymentMethod.Cash,
        FareGross = 1200, FareBase = 500, FareDistance = 600, FareTime = 100, FareServiceFee = 96
    };

    // ── CancelRideHandler ────────────────────────────────────────────────────

    [Fact]
    public async Task CancelRide_TerminalState_ThrowsConflict()
    {
        using var db = DbContextFactory.Create();
        var riderId = Guid.NewGuid().ToString();
        var trip = BuildTrip(riderId, JobState.Completed);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var handler = new CancelRideHandler(db, FakeServices.Realtime(), FakeServices.Push());
        var act = () => handler.Handle(
            new CancelRideCommand(riderId, trip.Id, "rider_cancel", null), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*terminal state*");
    }

    [Fact]
    public async Task CancelRide_NotFound_ThrowsKeyNotFound()
    {
        using var db = DbContextFactory.Create();
        var handler = new CancelRideHandler(db, FakeServices.Realtime(), FakeServices.Push());
        var act = () => handler.Handle(
            new CancelRideCommand("rider1", "nonexistent-trip", "rider_cancel", null), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task CancelRide_BeforeDriverArrival_ChargesNoFee()
    {
        using var db = DbContextFactory.Create();
        var riderId = Guid.NewGuid().ToString();
        var trip = BuildTrip(riderId, JobState.Broadcasting);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var handler = new CancelRideHandler(db, FakeServices.Realtime(), FakeServices.Push());
        var result = await handler.Handle(
            new CancelRideCommand(riderId, trip.Id, "changed_mind", null), CancellationToken.None);

        result.CancellationFee.Should().Be(0);
        result.Charged.Should().BeFalse();
        db.Trips.First(t => t.Id == trip.Id).JobState.Should().Be(JobState.CancelledByRider);
    }

    [Fact]
    public async Task CancelRide_AfterDriverArrival_AppliesCancellationFee()
    {
        using var db = DbContextFactory.Create();
        var riderId = Guid.NewGuid().ToString();
        var trip = BuildTrip(riderId, JobState.ArrivedAtPickup);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var handler = new CancelRideHandler(db, FakeServices.Realtime(), FakeServices.Push());
        var result = await handler.Handle(
            new CancelRideCommand(riderId, trip.Id, "rider_cancel", null), CancellationToken.None);

        result.CancellationFee.Should().Be(500);
        result.Charged.Should().BeTrue();
    }

    [Fact]
    public async Task CancelRide_CancelledState_ThrowsConflict()
    {
        using var db = DbContextFactory.Create();
        var riderId = Guid.NewGuid().ToString();
        var trip = BuildTrip(riderId, JobState.CancelledByRider);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var handler = new CancelRideHandler(db, FakeServices.Realtime(), FakeServices.Push());
        var act = () => handler.Handle(
            new CancelRideCommand(riderId, trip.Id, "rider_cancel", null), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(JobState.EnRouteToPickup)]
    [InlineData(JobState.Accepted)]
    [InlineData(JobState.Broadcasting)]
    public async Task CancelRide_ActiveStates_SucceedWithNoFee(JobState state)
    {
        using var db = DbContextFactory.Create();
        var riderId = Guid.NewGuid().ToString();
        var trip = BuildTrip(riderId, state);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var handler = new CancelRideHandler(db, FakeServices.Realtime(), FakeServices.Push());
        var result = await handler.Handle(
            new CancelRideCommand(riderId, trip.Id, "rider_cancel", null), CancellationToken.None);

        result.CancellationFee.Should().Be(0);
    }
}
