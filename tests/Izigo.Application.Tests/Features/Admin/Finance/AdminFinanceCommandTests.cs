using FluentAssertions;
using Izigo.Application.Features.Admin.Finance.Commands;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Xunit;

namespace Izigo.Application.Tests.Features.Admin.Finance;

public class AdminFinanceCommandTests
{
    private static Payout BuildPayout(string driverId, PaymentStatus status = PaymentStatus.Pending,
        long amount = 5000) => new()
    {
        DriverId      = driverId,
        GrossAmount   = amount,
        NetAmount     = amount,
        Currency      = "XOF",
        Market        = "ci",
        Status        = status,
        PayoutMethodId = "pm_test"
    };

    private static DriverWallet BuildDriverWallet(string driverId, long available = 5000) => new()
    {
        DriverId             = driverId,
        AvailableBalance     = available,
        PendingCashSettlement = 0
    };

    // ── ApprovePayoutHandler ──────────────────────────────────────────────────

    [Fact]
    public async Task ApprovePayout_NotFound_ReturnsNotFound()
    {
        using var db = DbContextFactory.Create();
        var handler = new ApprovePayoutHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new ApprovePayoutCommand("nonexistent", "staff1", "Staff", "ci"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("PAYOUT_NOT_FOUND");
    }

    [Fact]
    public async Task ApprovePayout_AlreadySucceeded_IsIdempotent()
    {
        using var db = DbContextFactory.Create();
        var driverId = Guid.NewGuid().ToString();
        var payout   = BuildPayout(driverId, PaymentStatus.Succeeded);
        db.Payouts.Add(payout);
        await db.SaveChangesAsync();

        var handler = new ApprovePayoutHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new ApprovePayoutCommand(payout.Id, "staff1", "Staff", "ci"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ApprovePayout_Processing_IsIdempotent()
    {
        using var db = DbContextFactory.Create();
        var driverId = Guid.NewGuid().ToString();
        var payout   = BuildPayout(driverId, PaymentStatus.Processing);
        db.Payouts.Add(payout);
        await db.SaveChangesAsync();

        var handler = new ApprovePayoutHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new ApprovePayoutCommand(payout.Id, "staff1", "Staff", "ci"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ApprovePayout_Cancelled_ReturnsPayoutNotPending()
    {
        using var db = DbContextFactory.Create();
        var driverId = Guid.NewGuid().ToString();
        var payout   = BuildPayout(driverId, PaymentStatus.Cancelled);
        db.Payouts.Add(payout);
        await db.SaveChangesAsync();

        var handler = new ApprovePayoutHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new ApprovePayoutCommand(payout.Id, "staff1", "Staff", "ci"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("PAYOUT_NOT_PENDING");
    }

    [Fact]
    public async Task ApprovePayout_Pending_SetsProcessingAndCreatesAttempt()
    {
        using var db = DbContextFactory.Create();
        var driverId = Guid.NewGuid().ToString();
        var payout   = BuildPayout(driverId, PaymentStatus.Pending);
        db.Payouts.Add(payout);
        await db.SaveChangesAsync();

        var handler = new ApprovePayoutHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new ApprovePayoutCommand(payout.Id, "staff1", "Staff", "ci"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var updated = db.Payouts.First(p => p.Id == payout.Id);
        updated.Status.Should().Be(PaymentStatus.Processing);
        updated.ApprovedBy.Should().Be("staff1");
        db.PayoutAttempts.Should().HaveCount(1);
    }

    // ── RejectPayoutHandler ───────────────────────────────────────────────────

    [Fact]
    public async Task RejectPayout_NotFound_ReturnsNotFound()
    {
        using var db = DbContextFactory.Create();
        var handler = new RejectPayoutHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new RejectPayoutCommand("nonexistent", "Insufficient docs", "staff1", "Staff", "ci"),
            CancellationToken.None);

        result.ErrorCode.Should().Be("PAYOUT_NOT_FOUND");
    }

    [Fact]
    public async Task RejectPayout_NotPending_ReturnsPayoutNotPending()
    {
        using var db = DbContextFactory.Create();
        var driverId = Guid.NewGuid().ToString();
        var payout   = BuildPayout(driverId, PaymentStatus.Processing);
        db.Payouts.Add(payout);
        await db.SaveChangesAsync();

        var handler = new RejectPayoutHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new RejectPayoutCommand(payout.Id, "Bad actor", "staff1", "Staff", "ci"),
            CancellationToken.None);

        result.ErrorCode.Should().Be("PAYOUT_NOT_PENDING");
    }

    [Fact]
    public async Task RejectPayout_Pending_CancelsPayout_AndReturnsFundsToDriverWallet()
    {
        using var db = DbContextFactory.Create();
        var driverId = Guid.NewGuid().ToString();
        var payout   = BuildPayout(driverId, PaymentStatus.Pending, amount: 3000);
        db.Payouts.Add(payout);
        db.DriverWallets.Add(BuildDriverWallet(driverId, available: 1000));
        await db.SaveChangesAsync();

        var handler = new RejectPayoutHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new RejectPayoutCommand(payout.Id, "Insufficient docs", "staff1", "Staff", "ci"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        db.Payouts.First(p => p.Id == payout.Id).Status.Should().Be(PaymentStatus.Cancelled);
        db.DriverWallets.First(w => w.DriverId == driverId).AvailableBalance.Should().Be(4000);
        db.DriverWalletTransactions.Should().HaveCount(1);
    }

    // ── RetryPayoutHandler ────────────────────────────────────────────────────

    [Fact]
    public async Task RetryPayout_NotFailed_ReturnsError()
    {
        using var db = DbContextFactory.Create();
        var driverId = Guid.NewGuid().ToString();
        var payout   = BuildPayout(driverId, PaymentStatus.Pending);
        db.Payouts.Add(payout);
        await db.SaveChangesAsync();

        var handler = new RetryPayoutHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new RetryPayoutCommand(payout.Id, "staff1", "Staff", "ci"),
            CancellationToken.None);

        result.ErrorCode.Should().Be("PAYOUT_NOT_FAILED");
    }

    [Fact]
    public async Task RetryPayout_Failed_SetsProcessingAndCreatesAttempt()
    {
        using var db = DbContextFactory.Create();
        var driverId = Guid.NewGuid().ToString();
        var payout   = BuildPayout(driverId, PaymentStatus.Failed);
        db.Payouts.Add(payout);
        await db.SaveChangesAsync();

        var handler = new RetryPayoutHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new RetryPayoutCommand(payout.Id, "staff1", "Staff", "ci"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        db.Payouts.First(p => p.Id == payout.Id).Status.Should().Be(PaymentStatus.Processing);
    }
}
