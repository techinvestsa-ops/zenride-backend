using FluentAssertions;
using Izigo.Application.Features.Admin.StaffManagement.Commands;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Enums;
using Xunit;

namespace Izigo.Application.Tests.Features.Admin.Staff;

public class StaffRoleHandlerTests
{
    private static Domain.Entities.Staff BuildStaff(string roleKey) => new()
    {
        Name               = "Staff",
        Email              = $"{roleKey}_{Guid.NewGuid():N}@test.com",
        RoleKey            = roleKey,
        PasswordHash       = "x",
        Status             = StaffStatus.Active,
        GrantedPermissions = [],
        RevokedPermissions = [],
        Markets            = ["ci"]
    };

    [Fact]
    public async Task ChangeRole_InsufficientRank_ReturnsError()
    {
        using var db = DbContextFactory.Create();

        // finance (rank 60) tries to demote operations (rank 60) — equal rank is not allowed
        var actor  = BuildStaff("finance");
        var target = BuildStaff("operations");
        db.Staff.AddRange(actor, target);
        await db.SaveChangesAsync();

        var handler = new ChangeStaffRoleHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(new ChangeStaffRoleCommand(
            TargetId: target.Id, ActorId: actor.Id,
            ActorRoleKey: "finance", ActorName: "Finance User",
            NewRoleKey: "read_only", Reason: "test", KeepOverrides: false),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INSUFFICIENT_RANK");
    }

    [Fact]
    public async Task ChangeRole_ClearsOverridesByDefault()
    {
        using var db = DbContextFactory.Create();

        var target = BuildStaff("support");
        target.GrantedPermissions.Add("trips.refund");

        // Need 2 super_admins so demotion doesn't trigger LAST_SUPER_ADMIN
        var actor      = BuildStaff("super_admin");
        var superAdmin2 = BuildStaff("super_admin");
        db.Staff.AddRange(target, actor, superAdmin2);
        await db.SaveChangesAsync();

        var handler = new ChangeStaffRoleHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(new ChangeStaffRoleCommand(
            TargetId: target.Id, ActorId: actor.Id,
            ActorRoleKey: "super_admin", ActorName: "Actor",
            NewRoleKey: "compliance", Reason: "promotion", KeepOverrides: false),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var updated = db.Staff.First(s => s.Id == target.Id);
        updated.RoleKey.Should().Be("compliance");
        updated.GrantedPermissions.Should().BeEmpty();
    }

    [Fact]
    public async Task ChangeRole_KeepsOverridesWhenFlagSet()
    {
        using var db = DbContextFactory.Create();

        var target      = BuildStaff("support");
        target.GrantedPermissions.Add("trips.refund");
        var actor       = BuildStaff("super_admin");
        var superAdmin2 = BuildStaff("super_admin");
        db.Staff.AddRange(target, actor, superAdmin2);
        await db.SaveChangesAsync();

        var handler = new ChangeStaffRoleHandler(db, FakeServices.Audit());
        await handler.Handle(new ChangeStaffRoleCommand(
            TargetId: target.Id, ActorId: actor.Id,
            ActorRoleKey: "super_admin", ActorName: "Actor",
            NewRoleKey: "compliance", Reason: "test", KeepOverrides: true),
            CancellationToken.None);

        db.Staff.First(s => s.Id == target.Id)
            .GrantedPermissions.Should().Contain("trips.refund");
    }

    [Fact]
    public async Task DeleteStaff_RefusesWhenHasAuditHistory()
    {
        using var db = DbContextFactory.Create();

        var target = BuildStaff("support");
        var actor  = BuildStaff("super_admin");
        db.Staff.AddRange(target, actor);
        db.AuditLogs.Add(new Domain.Entities.AuditLog
        {
            ActorId   = target.Id,
            ActorName = "Staff",
            Action    = Domain.Enums.AuditAction.Login,
            Market    = "ci"
        });
        await db.SaveChangesAsync();

        var handler = new DeleteStaffHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(new DeleteStaffCommand(
            TargetId: target.Id, ActorId: actor.Id,
            ActorRoleKey: "super_admin", ActorName: "Admin"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("HAS_AUDIT_HISTORY");
    }
}
