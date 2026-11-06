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
        db.Wallets.Add(new Izigo.Domain.Entities.Wallet { UserId = "usr_r1", Balance = 2000 });
        await db.SaveChangesAsync();

        var handler = new TipRideHandler(db);
        await handler.Handle(new TipRideCommand("usr_r1", trip.Id, 500), CancellationToken.None);

        db.Trips.First(t => t.Id == trip.Id).FareTip.Should().Be(500);
        db.DriverWallets.First(w => w.DriverId == "drv_d1").AvailableBalance.Should().Be(1500);
        db.DriverWalletTransactions.First().Amount.Should().Be(500);
        // The rider pays for it: money moves, it is not created.
        db.Wallets.First(w => w.UserId == "usr_r1").Balance.Should().Be(1500);
    }

    private static async Task<(Izigo.Infrastructure.Persistence.ApplicationDbContext db, Trip trip)> Tippable(long riderBalance)
    {
        var db = DbContextFactory.Create();
        var trip = new Trip { RiderId = "usr_r9", DriverId = "drv_d9", JobState = JobState.Completed, Market = "ci", Currency = "XOF", ServiceClass = ServiceClass.ZenCar };
        db.PlatformConfigs.Add(new PlatformConfig { Market = "ci", TippingEnabled = true });
        db.Trips.Add(trip);
        db.DriverWallets.Add(new DriverWallet { DriverId = "drv_d9", AvailableBalance = 0, Currency = "XOF" });
        db.Wallets.Add(new Izigo.Domain.Entities.Wallet { UserId = "usr_r9", Balance = riderBalance });
        await db.SaveChangesAsync();
        return (db, trip);
    }

    [Fact]
    public async Task Tip_WithoutEnoughWalletBalance_Throws_AndPaysNobody()
    {
        var (db, trip) = await Tippable(riderBalance: 100);
        using var _ = db;

        var act = () => new TipRideHandler(db).Handle(new TipRideCommand("usr_r9", trip.Id, 500), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Insufficient wallet balance*");
        db.DriverWallets.Single().AvailableBalance.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5000)]
    [InlineData(RideTipsMax + 1)]
    public async Task Tip_OutsideAllowedRange_Throws(long amount)
    {
        var (db, trip) = await Tippable(riderBalance: 50_000);
        using var _ = db;

        var act = () => new TipRideHandler(db).Handle(new TipRideCommand("usr_r9", trip.Id, amount), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        db.DriverWallets.Single().AvailableBalance.Should().Be(0);
    }

    private const long RideTipsMax = Izigo.Application.Features.Rides.Helpers.RideTips.MaxTip;

    [Fact]
    public async Task Tip_Twice_PaysTheDriverOnce()
    {
        var (db, trip) = await Tippable(riderBalance: 5000);
        using var _ = db;
        var handler = new TipRideHandler(db);

        await handler.Handle(new TipRideCommand("usr_r9", trip.Id, 500), CancellationToken.None);
        var again = () => handler.Handle(new TipRideCommand("usr_r9", trip.Id, 500), CancellationToken.None);

        await again.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already tipped*");
        db.DriverWallets.Single().AvailableBalance.Should().Be(500);
        db.Wallets.Single().Balance.Should().Be(4500);
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
