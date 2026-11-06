using FluentAssertions;
using Izigo.Application.Features.Driver.Commands;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Xunit;

namespace Izigo.Application.Tests.Features.Driver;

/// <summary>
/// Completing a trip must reach the driver's wallet even though wallets are
/// keyed by driver id and were never linked from the profile.
/// </summary>
public class TripCompletionWalletTests
{
    private static async Task<(Izigo.Infrastructure.Persistence.ApplicationDbContext db, Trip trip)> Arrived(PaymentMethod pay)
    {
        var db = DbContextFactory.Create();
        db.DriverProfiles.Add(new DriverProfile { UserId = "usr_drv", KycStatus = KycStatus.Approved });
        db.DriverWallets.Add(new DriverWallet { DriverId = "usr_drv", Currency = "XOF" });   // unlinked, as in production
        var trip = new Trip
        {
            RiderId = "usr_rdr", DriverId = "usr_drv", JobState = JobState.ArrivedAtDropoff, Vertical = Vertical.Ride,
            Market = "ci", Currency = "XOF", ServiceClass = ServiceClass.ZenCar, PaymentMethod = pay, FareGross = 4_000,
        };
        db.Trips.Add(trip);
        await db.SaveChangesAsync();
        return (db, trip);
    }

    [Fact]
    public async Task A_cash_trip_records_the_commission_the_driver_owes()
    {
        var (db, trip) = await Arrived(PaymentMethod.Cash);
        using var _ = db;

        await new CompleteJobHandler(db, FakeServices.Realtime(), FakeServices.Push())
            .Handle(new CompleteJobCommand("usr_drv", trip.Id), default);

        trip.CommissionAmount.Should().BeGreaterThan(0);
        db.DriverWallets.Single().PendingCashSettlement.Should().Be(trip.CommissionAmount);
    }

    [Fact]
    public async Task An_in_app_trip_credits_the_driver_their_earnings()
    {
        var (db, trip) = await Arrived(PaymentMethod.Wallet);
        using var _ = db;

        await new CompleteJobHandler(db, FakeServices.Realtime(), FakeServices.Push())
            .Handle(new CompleteJobCommand("usr_drv", trip.Id), default);

        db.DriverWallets.Single().AvailableBalance.Should().Be(trip.DriverEarnings);
        trip.DriverEarnings.Should().BeGreaterThan(0);
    }
}
