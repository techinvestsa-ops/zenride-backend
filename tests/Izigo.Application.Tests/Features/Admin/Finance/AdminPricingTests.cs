using FluentAssertions;
using Izigo.Application.Features.Admin.Finance.Commands;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Enums;
using Xunit;
using ZoneEntity = Izigo.Domain.Entities.Zone;
using FareRuleEntity = Izigo.Domain.Entities.FareRule;

namespace Izigo.Application.Tests.Features.Admin.Finance;

public class AdminPricingTests
{
    // ── UpdateFareRuleHandler ─────────────────────────────────────────────────

    [Fact]
    public async Task UpdateFareRule_InvalidServiceClass_ReturnsFalse()
    {
        using var db = DbContextFactory.Create();
        var handler = new UpdateFareRuleHandler(db, FakeServices.Audit());

        var result = await handler.Handle(
            new UpdateFareRuleCommand("ci", "InvalidClass",
                500, 100, 20, 1000, 50, 300, null, "staff1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_SERVICE_CLASS");
    }

    [Fact]
    public async Task UpdateFareRule_NoExistingRule_CreatesV1()
    {
        using var db = DbContextFactory.Create();
        var handler = new UpdateFareRuleHandler(db, FakeServices.Audit());

        var result = await handler.Handle(
            new UpdateFareRuleCommand("ci", "ZenCar",
                500, 100, 20, 1000, 50, 300, null, "staff1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        db.FareRules.Should().HaveCount(1);
        db.FareRules.First().Version.Should().Be(1);
        db.FareRules.First().IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateFareRule_ExistingActiveRule_DeactivatesOldAndCreatesNewVersion()
    {
        using var db = DbContextFactory.Create();
        var existing = new FareRuleEntity
        {
            ServiceClass = ServiceClass.ZenCar,
            Base = 400, PerKm = 80, PerMin = 15,
            Minimum = 800, WaitingPerMin = 40, CancellationFee = 250,
            IsActive = true, Version = 1, Market = "ci"
        };
        db.FareRules.Add(existing);
        await db.SaveChangesAsync();

        var handler = new UpdateFareRuleHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new UpdateFareRuleCommand("ci", "ZenCar",
                600, 120, 25, 1200, 60, 400, null, "staff1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        db.FareRules.Should().HaveCount(2);
        db.FareRules.First(r => r.Version == 1).IsActive.Should().BeFalse();
        var v2 = db.FareRules.First(r => r.Version == 2);
        v2.IsActive.Should().BeTrue();
        v2.Base.Should().Be(600);
        v2.PerKm.Should().Be(120);
    }

    // ── UpdateCommissionHandler ───────────────────────────────────────────────

    [Fact]
    public async Task UpdateCommission_NoExisting_CreatesV1()
    {
        using var db = DbContextFactory.Create();
        var handler = new UpdateCommissionHandler(db, FakeServices.Audit());

        var result = await handler.Handle(
            new UpdateCommissionCommand("ci", 0.25m, 0.035m, "ChopMonie Bonus",
                10_000, null, null, "staff1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        db.CommissionConfigs.Should().HaveCount(1);
        db.CommissionConfigs.First().Version.Should().Be(1);
        db.CommissionConfigs.First().CommissionRate.Should().Be(0.25m);
    }

    [Fact]
    public async Task UpdateCommission_Existing_CreatesNewVersion()
    {
        using var db = DbContextFactory.Create();
        db.CommissionConfigs.Add(new Domain.Entities.CommissionConfig
        {
            CommissionRate = 0.20m, BonusRate = 0.03m, Version = 1,
            Market = "ci", CashSettlementCap = 8000
        });
        await db.SaveChangesAsync();

        var handler = new UpdateCommissionHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new UpdateCommissionCommand("ci", 0.25m, 0.035m, null,
                null, null, null, "staff1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        db.CommissionConfigs.Should().HaveCount(2);
        db.CommissionConfigs.OrderByDescending(c => c.Version).First().CommissionRate.Should().Be(0.25m);
    }

    // ── UpdateSurgeHandler ────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateSurge_ZoneNotFound_ReturnsFalse()
    {
        using var db = DbContextFactory.Create();
        var handler = new UpdateSurgeHandler(db, FakeServices.Audit());

        var result = await handler.Handle(
            new UpdateSurgeCommand("nonexistent-zone", 1.3m, null,
                "High demand", "staff1", "Admin", "ci"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("ZONE_NOT_FOUND");
    }

    [Fact]
    public async Task UpdateSurge_Above16x_ReturnsSecondApproverRequired()
    {
        using var db = DbContextFactory.Create();
        var zone = new ZoneEntity
        {
            Name = "Zone A", Market = "ci",
            PolygonGeoJson = "{}", CenterLat = 5.3m, CenterLng = -4.0m
        };
        db.Zones.Add(zone);
        await db.SaveChangesAsync();

        var handler = new UpdateSurgeHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new UpdateSurgeCommand(zone.Id, 1.8m, null,
                "Extreme demand", "staff1", "Admin", "ci"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("SECOND_APPROVER_REQUIRED");
    }

    [Fact]
    public async Task UpdateSurge_Valid_SetsSurgeOnZone()
    {
        using var db = DbContextFactory.Create();
        var zone = new ZoneEntity
        {
            Name = "Zone B", Market = "ci",
            PolygonGeoJson = "{}", CenterLat = 5.3m, CenterLng = -4.0m
        };
        db.Zones.Add(zone);
        await db.SaveChangesAsync();

        var handler = new UpdateSurgeHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new UpdateSurgeCommand(zone.Id, 1.4m,
                DateTime.UtcNow.AddHours(2), "Weekend demand",
                "staff1", "Admin", "ci"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var surge = db.SurgeZones.First(s => s.ZoneId == zone.Id);
        surge.Multiplier.Should().Be(1.4m);
        surge.IsActive.Should().BeTrue();
        surge.OverriddenByStaffId.Should().Be("staff1");
    }

    [Fact]
    public async Task UpdateSurge_Multiplier1x_DeactivatesSurge()
    {
        using var db = DbContextFactory.Create();
        var zone = new ZoneEntity
        {
            Name = "Zone C", Market = "ci",
            PolygonGeoJson = "{}", CenterLat = 5.3m, CenterLng = -4.0m
        };
        db.Zones.Add(zone);
        db.SurgeZones.Add(new Domain.Entities.SurgeZone
        {
            ZoneId = zone.Id, Multiplier = 1.5m, IsActive = true
        });
        await db.SaveChangesAsync();

        var handler = new UpdateSurgeHandler(db, FakeServices.Audit());
        await handler.Handle(
            new UpdateSurgeCommand(zone.Id, 1.0m, null, "Surge ended", "staff1", "Admin", "ci"),
            CancellationToken.None);

        db.SurgeZones.First(s => s.ZoneId == zone.Id).IsActive.Should().BeFalse();
    }
}
