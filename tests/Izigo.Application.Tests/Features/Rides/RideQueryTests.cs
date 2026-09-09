using FluentAssertions;
using Izigo.Application.Features.Rides.Queries;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Xunit;

namespace Izigo.Application.Tests.Features.Rides;

public class RideQueryTests
{
    private static Trip BuildTrip(string riderId, JobState state = JobState.Accepted) => new()
    {
        Code          = "ZR-0001",
        Vertical      = Vertical.Ride,
        ServiceClass  = ServiceClass.ZenCar,
        RiderId       = riderId,
        Market        = "ci",
        Currency      = "XOF",
        JobState      = state,
        PickupLat     = 5.3m, PickupLng  = -4.0m, PickupLabel  = "Pickup",
        DropoffLat    = 5.4m, DropoffLng = -4.1m, DropoffLabel = "Dropoff",
        PaymentMethod = PaymentMethod.Cash,
        FareGross = 1200, FareBase = 500, FareDistance = 600, FareTime = 100,
        FareServiceFee = 96
    };

    // ── GetActiveRideHandler ──────────────────────────────────────────────────

    [Fact]
    public async Task GetActiveRide_NoActiveTrip_ReturnsNull()
    {
        using var db = DbContextFactory.Create();
        var handler = new GetActiveRideHandler(db,
            FakeServices.AppSettings(), FakeServices.QuoteSettings());

        var result = await handler.Handle(
            new GetActiveRideQuery(Guid.NewGuid().ToString()), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetActiveRide_CompletedTrip_ReturnsNull()
    {
        using var db = DbContextFactory.Create();
        var riderId = Guid.NewGuid().ToString();
        var trip = BuildTrip(riderId, JobState.Completed);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var handler = new GetActiveRideHandler(db,
            FakeServices.AppSettings(), FakeServices.QuoteSettings());

        var result = await handler.Handle(
            new GetActiveRideQuery(riderId), CancellationToken.None);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData(JobState.Broadcasting)]
    [InlineData(JobState.Accepted)]
    [InlineData(JobState.ArrivedAtPickup)]
    [InlineData(JobState.PickedUp)]
    public async Task GetActiveRide_ActiveTrip_ReturnsTripDetail(JobState state)
    {
        using var db = DbContextFactory.Create();
        var riderId = Guid.NewGuid().ToString();
        var trip = BuildTrip(riderId, state);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var handler = new GetActiveRideHandler(db,
            FakeServices.AppSettings(), FakeServices.QuoteSettings());

        var result = await handler.Handle(
            new GetActiveRideQuery(riderId), CancellationToken.None);

        result.Should().NotBeNull();
        result!.TripId.Should().Be(trip.Id);
    }

    // ── GetRideHandler ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetRide_NotFound_ThrowsKeyNotFound()
    {
        using var db = DbContextFactory.Create();
        var handler = new GetRideHandler(db,
            FakeServices.AppSettings(), FakeServices.QuoteSettings());

        var act = () => handler.Handle(
            new GetRideQuery("rider1", "nonexistent-trip"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*Ride not found*");
    }

    [Fact]
    public async Task GetRide_WrongRider_ThrowsKeyNotFound()
    {
        using var db = DbContextFactory.Create();
        var riderId = Guid.NewGuid().ToString();
        var trip = BuildTrip(riderId);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var handler = new GetRideHandler(db,
            FakeServices.AppSettings(), FakeServices.QuoteSettings());

        var act = () => handler.Handle(
            new GetRideQuery("different-rider", trip.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task GetRide_Found_ReturnsCorrectDetail()
    {
        using var db = DbContextFactory.Create();
        var riderId = Guid.NewGuid().ToString();
        var trip = BuildTrip(riderId, JobState.Completed);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var handler = new GetRideHandler(db,
            FakeServices.AppSettings(), FakeServices.QuoteSettings());

        var result = await handler.Handle(
            new GetRideQuery(riderId, trip.Id), CancellationToken.None);

        result.TripId.Should().Be(trip.Id);
        result.Code.Should().Be("ZR-0001");
        result.Vertical.Should().Be("ride");
    }

    // ── GetRidesHandler ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetRides_NoTrips_ReturnsEmptyList()
    {
        using var db = DbContextFactory.Create();
        var handler = new GetRidesHandler(db);

        var (items, total) = await handler.Handle(
            new GetRidesQuery(Guid.NewGuid().ToString(), null, null, null, null, null, 1, 20),
            CancellationToken.None);

        items.Should().BeEmpty();
        total.Should().Be(0);
    }

    [Fact]
    public async Task GetRides_ReturnsOnlyRiderTrips()
    {
        using var db = DbContextFactory.Create();
        var riderId  = Guid.NewGuid().ToString();
        var otherId  = Guid.NewGuid().ToString();

        db.Trips.Add(BuildTrip(riderId, JobState.Completed));
        db.Trips.Add(BuildTrip(riderId, JobState.Completed));
        db.Trips.Add(BuildTrip(otherId, JobState.Completed));
        await db.SaveChangesAsync();

        var handler = new GetRidesHandler(db);
        var (items, total) = await handler.Handle(
            new GetRidesQuery(riderId, null, null, null, null, null, 1, 20),
            CancellationToken.None);

        items.Should().HaveCount(2);
        total.Should().Be(2);
    }

    [Fact]
    public async Task GetRides_FilterByVertical_ReturnsMatchingOnly()
    {
        using var db = DbContextFactory.Create();
        var riderId = Guid.NewGuid().ToString();

        var rideTrip = BuildTrip(riderId, JobState.Completed);
        rideTrip.Vertical = Vertical.Ride;

        var coRideTrip = BuildTrip(riderId, JobState.Completed);
        coRideTrip.Vertical = Vertical.CoRide;

        db.Trips.AddRange(rideTrip, coRideTrip);
        await db.SaveChangesAsync();

        var handler = new GetRidesHandler(db);
        var (items, total) = await handler.Handle(
            new GetRidesQuery(riderId, null, "Ride", null, null, null, 1, 20),
            CancellationToken.None);

        items.Should().HaveCount(1);
        total.Should().Be(1);
        items[0].Vertical.Should().Be("ride");
    }

    [Fact]
    public async Task GetRides_Pagination_RespectsPageAndPerPage()
    {
        using var db = DbContextFactory.Create();
        var riderId = Guid.NewGuid().ToString();

        for (var i = 0; i < 5; i++)
            db.Trips.Add(BuildTrip(riderId, JobState.Completed));
        await db.SaveChangesAsync();

        var handler = new GetRidesHandler(db);
        var (items, total) = await handler.Handle(
            new GetRidesQuery(riderId, null, null, null, null, null, 1, 3),
            CancellationToken.None);

        items.Should().HaveCount(3);
        total.Should().Be(5);
    }

    // ── GetReceiptHandler ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetReceipt_NotFound_ThrowsKeyNotFound()
    {
        using var db = DbContextFactory.Create();
        var handler = new GetReceiptHandler(db);

        var act = () => handler.Handle(
            new GetReceiptQuery("rider1", "nonexistent"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*Ride not found*");
    }

    [Fact]
    public async Task GetReceipt_Found_ReturnsCorrectFareBreakdown()
    {
        using var db = DbContextFactory.Create();
        var riderId = Guid.NewGuid().ToString();
        var trip = BuildTrip(riderId, JobState.Completed);
        trip.FareTip = 200;
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var handler = new GetReceiptHandler(db);
        var result = await handler.Handle(
            new GetReceiptQuery(riderId, trip.Id), CancellationToken.None);

        result.TripId.Should().Be(trip.Id);
        result.Code.Should().Be("ZR-0001");
        result.Fare.Total.Should().Be(
            trip.FareGross + trip.FareServiceFee - trip.FareDiscount);
        result.Fare.Tip.Should().Be(200);
        result.Fare.Currency.Should().Be("XOF");
    }
}
