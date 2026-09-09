using FluentAssertions;
using Izigo.Application.Features.Driver.Commands;
using Izigo.Application.Features.Driver.Dtos;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Xunit;

namespace Izigo.Application.Tests.Features.Driver;

public class DriverStatusHandlerTests
{
    // DriverWalletId is a shadow FK on DriverProfile, so we must set the
    // DriverWallet navigation property (not add them separately).
    private static DriverProfile BuildProfile(string userId,
        KycStatus kycStatus = KycStatus.Approved,
        bool onboardingComplete = true,
        long pendingCash = 0,
        bool isOnline = false) => new()
    {
        UserId             = userId,
        KycStatus          = kycStatus,
        OnboardingComplete = onboardingComplete,
        IsOnline           = isOnline,
        VerticalsAllowed   = [Vertical.Ride],
        DriverWallet       = new DriverWallet
        {
            DriverId              = userId,
            PendingCashSettlement = pendingCash
        }
    };

    [Fact]
    public async Task SetStatus_Online_KycNotApproved_ThrowsForbidden()
    {
        using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid().ToString();
        db.DriverProfiles.Add(BuildProfile(userId, kycStatus: KycStatus.Pending));
        await db.SaveChangesAsync();

        var handler = new SetDriverStatusHandler(db);
        var act = () => handler.Handle(
            new SetDriverStatusCommand(userId, new SetStatusRequest(true, null)),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*KYC must be approved*");
    }

    [Fact]
    public async Task SetStatus_Online_OnboardingNotComplete_ThrowsForbidden()
    {
        using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid().ToString();
        db.DriverProfiles.Add(BuildProfile(userId, onboardingComplete: false));
        await db.SaveChangesAsync();

        var handler = new SetDriverStatusHandler(db);
        var act = () => handler.Handle(
            new SetDriverStatusCommand(userId, new SetStatusRequest(true, null)),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Onboarding is not complete*");
    }

    [Fact]
    public async Task SetStatus_Online_CashCapExceeded_ThrowsForbidden()
    {
        using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid().ToString();
        db.DriverProfiles.Add(BuildProfile(userId, pendingCash: 10_000));
        await db.SaveChangesAsync();

        var handler = new SetDriverStatusHandler(db);
        var act = () => handler.Handle(
            new SetDriverStatusCommand(userId, new SetStatusRequest(true, null)),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Cash settlement balance*");
    }

    [Fact]
    public async Task SetStatus_Online_Success_SetsIsOnlineTrue()
    {
        using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid().ToString();
        db.DriverProfiles.Add(BuildProfile(userId));
        await db.SaveChangesAsync();

        var handler = new SetDriverStatusHandler(db);
        var result = await handler.Handle(
            new SetDriverStatusCommand(userId, new SetStatusRequest(true, null)),
            CancellationToken.None);

        result.IsOnline.Should().BeTrue();
        db.DriverProfiles.First(d => d.UserId == userId).IsOnline.Should().BeTrue();
    }

    [Fact]
    public async Task SetStatus_Offline_Success_SetsIsOnlineFalse()
    {
        using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid().ToString();
        db.DriverProfiles.Add(BuildProfile(userId, isOnline: true));
        await db.SaveChangesAsync();

        var handler = new SetDriverStatusHandler(db);
        var result = await handler.Handle(
            new SetDriverStatusCommand(userId, new SetStatusRequest(false, null)),
            CancellationToken.None);

        result.IsOnline.Should().BeFalse();
    }

    [Fact]
    public async Task SetStatus_CashJustBelowCap_AllowsGoingOnline()
    {
        using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid().ToString();
        db.DriverProfiles.Add(BuildProfile(userId, pendingCash: 9_999));
        await db.SaveChangesAsync();

        var handler = new SetDriverStatusHandler(db);
        var result = await handler.Handle(
            new SetDriverStatusCommand(userId, new SetStatusRequest(true, null)),
            CancellationToken.None);

        result.IsOnline.Should().BeTrue();
        result.CashCapBlocked.Should().BeFalse();
    }

    [Fact]
    public async Task SetStatus_ProfileNotFound_ThrowsKeyNotFound()
    {
        using var db = DbContextFactory.Create();
        var handler = new SetDriverStatusHandler(db);
        var act = () => handler.Handle(
            new SetDriverStatusCommand("nonexistent", new SetStatusRequest(true, null)),
            CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
