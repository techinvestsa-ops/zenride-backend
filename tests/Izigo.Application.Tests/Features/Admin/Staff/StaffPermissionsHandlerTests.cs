using FluentAssertions;
using Izigo.Application.Features.Admin.StaffManagement.Commands;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Enums;
using Xunit;

namespace Izigo.Application.Tests.Features.Admin.Staff;

public class StaffPermissionsHandlerTests
{
    private static Domain.Entities.Staff BuildStaff(string roleKey,
        List<string>? granted = null, List<string>? revoked = null) => new()
    {
        Name               = "Test Staff",
        Email              = $"{roleKey}_{Guid.NewGuid():N}@test.com",
        RoleKey            = roleKey,
        PasswordHash       = "x",
        Status             = StaffStatus.Active,
        GrantedPermissions = granted ?? [],
        RevokedPermissions = revoked ?? [],
        Markets            = ["ci"]
    };

    [Fact]
    public async Task SetPermissions_CannotGrantPermissionActorDoesNotHold()
    {
        using var db = DbContextFactory.Create();

        var actor  = BuildStaff("support");    // support does NOT have wallets.adjust
        var target = BuildStaff("read_only");

        db.Staff.AddRange(actor, target);
        await db.SaveChangesAsync();

        var handler = new SetStaffPermissionsHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(new SetStaffPermissionsCommand(
            TargetId: target.Id, ActorId: actor.Id, ActorRoleKey: "support",
            ActorName: "Support User",
            Granted: ["wallets.adjust"], Revoked: [],
            Reason: "test"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("CANNOT_GRANT_WHAT_YOU_LACK");
    }

    [Fact]
    public async Task SetPermissions_CanGrantPermissionActorHolds()
    {
        using var db = DbContextFactory.Create();

        var actor  = BuildStaff("admin");      // admin has trips.refund
        var target = BuildStaff("support");

        db.Staff.AddRange(actor, target);
        await db.SaveChangesAsync();

        var handler = new SetStaffPermissionsHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(new SetStaffPermissionsCommand(
            TargetId: target.Id, ActorId: actor.Id, ActorRoleKey: "admin",
            ActorName: "Admin User",
            Granted: ["trips.refund"], Revoked: [],
            Reason: "test"), CancellationToken.None);

        result.Success.Should().BeTrue();
        db.Staff.First(s => s.Id == target.Id)
            .GrantedPermissions.Should().Contain("trips.refund");
    }

    [Fact]
    public async Task SetPermissions_CannotTargetSelf()
    {
        using var db = DbContextFactory.Create();

        var actor = BuildStaff("admin");
        db.Staff.Add(actor);
        await db.SaveChangesAsync();

        var handler = new SetStaffPermissionsHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(new SetStaffPermissionsCommand(
            TargetId: actor.Id, ActorId: actor.Id, ActorRoleKey: "admin",
            ActorName: "Admin",
            Granted: ["dashboard.view"], Revoked: [],
            Reason: "test"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("CANNOT_TARGET_SELF");
    }
}
