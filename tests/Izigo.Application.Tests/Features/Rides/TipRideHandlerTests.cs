using FluentAssertions;
using Izigo.Application.Features.Rides.Commands;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Xunit;

namespace Izigo.Application.Tests.Features.Rides;

public class TipRideHandlerTests
{
    [Fact]
    public async Task Tip_WhenTippingEnabled_UpdatesFareTipAndCreditsDriver()
    {
        using var db = DbContextFactory.Create();

        var config = new PlatformConfig { Market = "ci", TippingEnabled = true };
        var trip   = new Trip { RiderId = "usr_r1", JobState = JobState.Completed, Market = "ci", Currency = "XOF", ServiceClass = ServiceClass.ZenCar };
        var wallet = new DriverWallet { DriverId = "drv_d1", AvailableBalance = 1000, Currency = "XOF" };
        trip.DriverId = "drv_d1";

        db.PlatformConfigs.Add(config);
        db.Trips.Add(trip);
        db.DriverWallets.Add(wallet);
        await db.SaveChangesAsync();

        var handler = new TipRideHandler(db);
        await handler.Handle(new TipRideCommand("usr_r1", trip.Id, 500), CancellationToken.None);

        db.Trips.First(t => t.Id == trip.Id).FareTip.Should().Be(500);
        db.DriverWallets.First(w => w.DriverId == "drv_d1").AvailableBalance.Should().Be(1500);
        db.DriverWalletTransactions.First().Amount.Should().Be(500);
    }

    [Fact]
    public async Task Tip_WhenTippingDisabled_Throws()
    {
        using var db = DbContextFactory.Create();

        var config = new PlatformConfig { Market = "ci", TippingEnabled = false };
        var trip   = new Trip { RiderId = "usr_r2", JobState = JobState.Completed, Market = "ci", Currency = "XOF", ServiceClass = ServiceClass.ZenCar };

        db.PlatformConfigs.Add(config);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var handler = new TipRideHandler(db);
        var act     = () => handler.Handle(new TipRideCommand("usr_r2", trip.Id, 500), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Tipping is not enabled*");
    }

    [Fact]
    public async Task Tip_OnNonCompletedTrip_Throws()
    {
        using var db = DbContextFactory.Create();

        var config = new PlatformConfig { Market = "ci", TippingEnabled = true };
        var trip   = new Trip { RiderId = "usr_r3", JobState = JobState.PickedUp, Market = "ci", Currency = "XOF", ServiceClass = ServiceClass.ZenCar };

        db.PlatformConfigs.Add(config);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var handler = new TipRideHandler(db);
        var act     = () => handler.Handle(new TipRideCommand("usr_r3", trip.Id, 500), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Can only tip on completed trips*");
    }
}
