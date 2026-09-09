using FluentAssertions;
using Izigo.Application.Features.Admin.Finance.Commands;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Xunit;

namespace Izigo.Application.Tests.Features.Admin.Finance;

public class PayoutIdempotencyTests
{
    [Fact]
    public async Task ApprovePayout_IsIdempotent_SecondApproveIsNoOp()
    {
        using var db = DbContextFactory.Create();

        var payout = new Payout
        {
            DriverId        = "drv_1",
            PayoutMethodId  = "pm_1",
            GrossAmount     = 5000,
            NetAmount       = 5000,
            Status          = PaymentStatus.Succeeded,  // already approved
            IdempotencyKey  = "idem_already"
        };
        db.Payouts.Add(payout);
        await db.SaveChangesAsync();

        var handler = new ApprovePayoutHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new ApprovePayoutCommand(payout.Id, "stf_1", "Admin", "ci"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        db.Payouts.First(p => p.Id == payout.Id).Status
            .Should().Be(PaymentStatus.Succeeded);
    }

    [Fact]
    public async Task ApprovePayout_NewApproval_MovesOutOfPending()
    {
        using var db = DbContextFactory.Create();

        var payout = new Payout
        {
            DriverId       = "drv_2",
            PayoutMethodId = "pm_2",
            GrossAmount    = 3000,
            NetAmount      = 3000,
            Status         = PaymentStatus.Pending
        };
        db.Payouts.Add(payout);
        await db.SaveChangesAsync();

        var handler = new ApprovePayoutHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new ApprovePayoutCommand(payout.Id, "stf_1", "Admin", "ci"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        db.Payouts.First(p => p.Id == payout.Id).Status
            .Should().NotBe(PaymentStatus.Pending);
    }
}
