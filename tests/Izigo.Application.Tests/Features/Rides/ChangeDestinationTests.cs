using FluentAssertions;
using Izigo.Application.Features.Quotes.Helpers;
using Izigo.Application.Features.Rides.Commands;
using Izigo.Application.Features.Rides.Dtos;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Xunit;

namespace Izigo.Application.Tests.Features.Rides;

/// <summary>A new destination is a new price: the rider pays for the route driven.</summary>
public class ChangeDestinationTests
{
    private static ChangeDestinationHandler Handler(Izigo.Infrastructure.Persistence.ApplicationDbContext db) =>
        new(db, FakeServices.Geo(), FakeServices.AppSettings(), FakeServices.QuoteSettings(), FakeServices.Realtime());

    private static Trip InProgress(PaymentMethod pay) => new()
    {
        RiderId = "usr_dest", DriverId = "drv_dest", JobState = JobState.PickedUp,
        Market = "ci", Currency = "XOF", ServiceClass = ServiceClass.ZenCar, PaymentMethod = pay,
        PickupLat = 5.30m, PickupLng = -4.00m, DropoffLat = 5.301m, DropoffLng = -4.001m,
        DistanceM = 200, FareGross = 2_000,
    };

    [Fact]
    public async Task Changing_destination_reprices_the_trip_for_the_new_route()
    {
        using var db = DbContextFactory.Create();
        var trip = InProgress(PaymentMethod.Cash);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        await Handler(db).Handle(new ChangeDestinationCommand("usr_dest", trip.Id,
            new ChangeDestinationRequest("Plateau", 5.34, -4.02, null)), CancellationToken.None);

        // The geo fake routes 5 km / 15 min; price it like a quote would.
        var expected = FareCalculator.Calculate(FareCalculator.DefaultRule(ServiceClass.ZenCar), 5000, 900, 900);
        var saved = db.Trips.Single();
        saved.FareGross.Should().Be(expected.Total);
        saved.DistanceM.Should().Be(5000);
        saved.DropoffLabel.Should().Be("Plateau");
    }

    [Fact]
    public async Task Wallet_ride_that_cannot_cover_the_new_price_keeps_its_destination()
    {
        using var db = DbContextFactory.Create();
        var trip = InProgress(PaymentMethod.Wallet);
        db.Trips.Add(trip);
        db.Wallets.Add(new Izigo.Domain.Entities.Wallet { UserId = "usr_dest", Balance = 2_000 });
        await db.SaveChangesAsync();

        var act = () => Handler(db).Handle(new ChangeDestinationCommand("usr_dest", trip.Id,
            new ChangeDestinationRequest("Plateau", 5.34, -4.02, null)), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Insufficient wallet balance*");
        db.Trips.Single().FareGross.Should().Be(2_000);
    }
}
