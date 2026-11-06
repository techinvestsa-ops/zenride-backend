using FluentAssertions;
using Izigo.Application.Common.Helpers;
using Izigo.Application.Features.Admin.Finance.Commands;
using Izigo.Application.Features.Driver.Commands;
using Izigo.Application.Features.Driver.Dtos;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Xunit;

namespace Izigo.Application.Tests.Features.Driver;

/// <summary>A driver's cash debt only goes down when ops have the money.</summary>
public class CashSettlementClaimTests
{
    private static async Task<Izigo.Infrastructure.Persistence.ApplicationDbContext> Owing(long owed)
    {
        var db = DbContextFactory.Create();
        db.DriverWallets.Add(new DriverWallet { DriverId = "usr_cash", PendingCashSettlement = owed, Currency = "XOF" });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Reporting_a_payment_does_not_clear_the_debt()
    {
        using var db = await Owing(8_000);

        var result = await new PayCashSettlementHandler(db)
            .Handle(new PayCashSettlementCommand("usr_cash", new PayCashSettlementRequest(8_000, "OM-123")), default);

        result.Outstanding.Should().Be(8_000);
        result.PendingConfirmation.Should().Be(8_000);
        db.DriverWallets.Single().PendingCashSettlement.Should().Be(8_000);
    }

    [Fact]
    public async Task Reports_cannot_add_up_to_more_than_is_owed()
    {
        using var db = await Owing(8_000);
        var handler = new PayCashSettlementHandler(db);
        await handler.Handle(new PayCashSettlementCommand("usr_cash", new PayCashSettlementRequest(6_000, null)), default);

        var again = () => handler.Handle(new PayCashSettlementCommand("usr_cash", new PayCashSettlementRequest(3_000, null)), default);

        await again.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Ops_recording_the_money_clears_the_debt_and_confirms_the_report()
    {
        using var db = await Owing(8_000);
        await new PayCashSettlementHandler(db)
            .Handle(new PayCashSettlementCommand("usr_cash", new PayCashSettlementRequest(8_000, "OM-123")), default);

        var result = await new RecordCashSettlementHandler(db, FakeServices.Audit()).Handle(
            new RecordCashSettlementCommand("usr_cash", 8_000, "orange_money", "OM-123", "stf_1", "ops@izigo", "ci"), default);

        result.Success.Should().BeTrue();
        db.DriverWallets.Single().PendingCashSettlement.Should().Be(0);
        (await CashSettlementClaims.PendingAsync(db, db.DriverWallets.Single().Id, default)).Should().Be(0);
    }
}
