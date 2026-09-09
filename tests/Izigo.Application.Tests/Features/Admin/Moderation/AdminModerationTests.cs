using FluentAssertions;
using Izigo.Application.Features.Admin.Riders.Commands;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Enums;
using Xunit;
using UserEntity = Izigo.Domain.Entities.User;
using WalletEntity = Izigo.Domain.Entities.Wallet;
using TripEntity = Izigo.Domain.Entities.Trip;
using RefreshTokenEntity = Izigo.Domain.Entities.RefreshToken;

namespace Izigo.Application.Tests.Features.Admin.Moderation;

public class AdminModerationTests
{
    private static UserEntity BuildRider(string phone = "+2250700000001") => new()
    {
        FirstName = "Rider",
        LastName  = "Test",
        Phone     = phone,
        Role      = UserRole.Rider,
        Rating    = 5.0m,
        Status    = UserStatus.Active
    };

    private static TripEntity BuildActiveTrip(string riderId) => new()
    {
        Code          = "ZR-9999",
        RiderId       = riderId,
        Market        = "ci",
        Currency      = "XOF",
        Vertical      = Vertical.Ride,
        ServiceClass  = ServiceClass.ZenCar,
        JobState      = JobState.Accepted,
        PickupLat = 5.3m, PickupLng = -4.0m, PickupLabel = "A",
        DropoffLat = 5.4m, DropoffLng = -4.1m, DropoffLabel = "B",
        PaymentMethod = PaymentMethod.Cash
    };

    // ── SuspendRiderHandler ───────────────────────────────────────────────────

    [Fact]
    public async Task SuspendRider_NotFound_ReturnsFalse()
    {
        using var db = DbContextFactory.Create();
        var handler = new SuspendRiderHandler(db, FakeServices.Audit());

        var result = await handler.Handle(
            new SuspendRiderCommand("nonexistent", "suspend", "Bad behavior",
                null, "staff1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("RIDER_NOT_FOUND");
    }

    [Fact]
    public async Task SuspendRider_Suspend_SetsStatusSuspended()
    {
        using var db = DbContextFactory.Create();
        var rider   = BuildRider();
        db.Users.Add(rider);
        await db.SaveChangesAsync();

        var handler = new SuspendRiderHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new SuspendRiderCommand(rider.Id, "suspend", "Policy violation",
                DateTime.UtcNow.AddDays(7), "staff1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        db.Users.Find(rider.Id)!.Status.Should().Be(UserStatus.Suspended);
        db.Users.Find(rider.Id)!.SuspensionReason.Should().Be("Policy violation");
    }

    [Fact]
    public async Task SuspendRider_Reinstate_SetsStatusActive()
    {
        using var db = DbContextFactory.Create();
        var rider   = BuildRider();
        rider.Status          = UserStatus.Suspended;
        rider.SuspensionReason = "Temp ban";
        db.Users.Add(rider);
        await db.SaveChangesAsync();

        var handler = new SuspendRiderHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new SuspendRiderCommand(rider.Id, "reinstate", "Appeal approved",
                null, "staff1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var updated = db.Users.Find(rider.Id)!;
        updated.Status.Should().Be(UserStatus.Active);
        updated.SuspensionReason.Should().BeNull();
    }

    // ── FlagRiderHandler ──────────────────────────────────────────────────────

    [Fact]
    public async Task FlagRider_NotFound_ReturnsFalse()
    {
        using var db = DbContextFactory.Create();
        var handler = new FlagRiderHandler(db, FakeServices.Audit());

        var result = await handler.Handle(
            new FlagRiderCommand("nonexistent", "Fraud", "high", "staff1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("RIDER_NOT_FOUND");
    }

    [Fact]
    public async Task FlagRider_Success_SetsFlaggedAndSeverity()
    {
        using var db = DbContextFactory.Create();
        var rider   = BuildRider();
        db.Users.Add(rider);
        await db.SaveChangesAsync();

        var handler = new FlagRiderHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new FlagRiderCommand(rider.Id, "Suspicious activity", "medium", "staff1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var updated = db.Users.Find(rider.Id)!;
        updated.IsFlagged.Should().BeTrue();
        updated.FlagReason.Should().Be("Suspicious activity");
        updated.FlagSeverity.Should().Be("medium");
    }

    // ── BlockRiderHandler ─────────────────────────────────────────────────────

    [Fact]
    public async Task BlockRider_NotFound_ReturnsFalse()
    {
        using var db = DbContextFactory.Create();
        var handler = new BlockRiderHandler(db, FakeServices.Audit(), FakeServices.Realtime());

        var result = await handler.Handle(
            new BlockRiderCommand("nonexistent", "Fraud", false, "staff1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("RIDER_NOT_FOUND");
    }

    [Fact]
    public async Task BlockRider_Success_SetsBlockedAndRevokesTokens()
    {
        using var db = DbContextFactory.Create();
        var rider = BuildRider();
        db.Users.Add(rider);
        db.RefreshTokens.Add(new RefreshTokenEntity
        {
            UserId = rider.Id, Token = "tok1", ExpiresAt = DateTime.UtcNow.AddDays(30)
        });
        await db.SaveChangesAsync();

        var handler = new BlockRiderHandler(db, FakeServices.Audit(), FakeServices.Realtime());
        var result  = await handler.Handle(
            new BlockRiderCommand(rider.Id, "Fraud", false, "staff1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        db.Users.Find(rider.Id)!.Status.Should().Be(UserStatus.Blocked);
        db.RefreshTokens.First().IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task BlockRider_WithWalletBalance_ReportsStrandedBalance()
    {
        using var db = DbContextFactory.Create();
        var rider = BuildRider();
        db.Users.Add(rider);
        db.Wallets.Add(new WalletEntity
        {
            UserId = rider.Id, Balance = 5000, Currency = "XOF",
            MinTopup = 500, MaxBalance = 500_000
        });
        await db.SaveChangesAsync();

        var handler = new BlockRiderHandler(db, FakeServices.Audit(), FakeServices.Realtime());
        var result  = await handler.Handle(
            new BlockRiderCommand(rider.Id, "Fraud", false, "staff1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        // Data should indicate stranded wallet balance
        result.Data.Should().NotBeNull();
    }

    [Fact]
    public async Task BlockRider_CancelActiveTrip_CancelsTrip()
    {
        using var db = DbContextFactory.Create();
        var rider = BuildRider();
        db.Users.Add(rider);
        var trip = BuildActiveTrip(rider.Id);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var handler = new BlockRiderHandler(db, FakeServices.Audit(), FakeServices.Realtime());
        await handler.Handle(
            new BlockRiderCommand(rider.Id, "Fraud", true, "staff1", "Admin"),
            CancellationToken.None);

        db.Trips.Find(trip.Id)!.JobState.Should().Be(JobState.CancelledByAdmin);
    }

    // ── UnblockRiderHandler ───────────────────────────────────────────────────

    [Fact]
    public async Task UnblockRider_NotFound_ReturnsFalse()
    {
        using var db = DbContextFactory.Create();
        var handler = new UnblockRiderHandler(db, FakeServices.Audit());

        var result = await handler.Handle(
            new UnblockRiderCommand("nonexistent", "Appeal", "staff1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("RIDER_NOT_FOUND");
    }

    [Fact]
    public async Task UnblockRider_Success_SetsActiveAndClearsSuspensionReason()
    {
        using var db = DbContextFactory.Create();
        var rider = BuildRider();
        rider.Status           = UserStatus.Blocked;
        rider.SuspensionReason = "Previous offence";
        db.Users.Add(rider);
        await db.SaveChangesAsync();

        var handler = new UnblockRiderHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new UnblockRiderCommand(rider.Id, "Appeal approved", "staff1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var updated = db.Users.Find(rider.Id)!;
        updated.Status.Should().Be(UserStatus.Active);
        updated.SuspensionReason.Should().BeNull();
    }

    // ── DeleteRiderHandler ────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteRider_NotFound_ReturnsFalse()
    {
        using var db = DbContextFactory.Create();
        var handler = new DeleteRiderHandler(db, FakeServices.Audit());

        var result = await handler.Handle(
            new DeleteRiderCommand("nonexistent", "staff1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("RIDER_NOT_FOUND");
    }

    [Fact]
    public async Task DeleteRider_WalletBalanceExists_ReturnsFalse()
    {
        using var db = DbContextFactory.Create();
        var rider = BuildRider();
        db.Users.Add(rider);
        db.Wallets.Add(new WalletEntity
        {
            UserId = rider.Id, Balance = 500, Currency = "XOF",
            MinTopup = 500, MaxBalance = 500_000
        });
        await db.SaveChangesAsync();

        var handler = new DeleteRiderHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new DeleteRiderCommand(rider.Id, "staff1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("WALLET_BALANCE_EXISTS");
    }

    [Fact]
    public async Task DeleteRider_OpenTrip_ReturnsFalse()
    {
        using var db = DbContextFactory.Create();
        var rider = BuildRider();
        db.Users.Add(rider);
        db.Trips.Add(BuildActiveTrip(rider.Id));
        await db.SaveChangesAsync();

        var handler = new DeleteRiderHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new DeleteRiderCommand(rider.Id, "staff1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("OPEN_TRIP_EXISTS");
    }

    [Fact]
    public async Task DeleteRider_NoBlockers_AnonymisesUser()
    {
        using var db = DbContextFactory.Create();
        var rider = BuildRider();
        db.Users.Add(rider);
        await db.SaveChangesAsync();

        var handler = new DeleteRiderHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new DeleteRiderCommand(rider.Id, "staff1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var updated = db.Users.Find(rider.Id)!;
        updated.FirstName.Should().Be("Deleted");
        updated.Phone.Should().StartWith("+00000000");
        updated.Status.Should().Be(UserStatus.Blocked);
        updated.DeletionRequested.Should().BeTrue();
    }
}
