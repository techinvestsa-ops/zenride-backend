using FluentAssertions;
using Izigo.Application.Features.Admin.StaffManagement.Commands;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Enums;
using Xunit;

namespace Izigo.Application.Tests.Features.Admin.Staff;

public class InviteStaffHandlerTests
{
    [Fact]
    public async Task InviteStaff_CreatesPendingAccountUntilAccepted()
    {
        using var db = DbContextFactory.Create();
        var handler = new InviteStaffHandler(
            db, FakeServices.Audit(), FakeServices.Hasher(), FakeServices.Email(),
            FakeServices.AdminAuthSettings());

        var result = await handler.Handle(
            new InviteStaffCommand("Ada Nwosu", "ada@zenride.app", null, "admin", ["ci"],
                "super1", "super_admin", "Super Admin"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var staff = db.Staff.Single(s => s.Email == "ada@zenride.app");
        staff.Status.Should().Be(StaffStatus.Pending);
    }
}
