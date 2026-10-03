using FluentAssertions;
using Izigo.Application.Features.Safety.Commands;
using Izigo.Application.Features.Safety.Dtos;
using Izigo.Application.Features.Safety.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Izigo.Application.Tests.Helpers;
using Xunit;

namespace Izigo.Application.Tests.Features.Safety;

public class AccountReportTests
{
    [Fact]
    public async Task FourthReport_SuspendsTheDriver_AndOpensAnAppeal()
    {
        using var db = DbContextFactory.Create();
        var rider = new User { FirstName = "Ama", LastName = "Kone", Phone = "+2250701000001", Role = UserRole.Rider, Status = UserStatus.Active };
        var driver = new User { FirstName = "Koffi", LastName = "Yao", Phone = "+2250701000002", Role = UserRole.Driver, Status = UserStatus.Active, Email = "koffi@example.com" };
        db.Users.AddRange(rider, driver);
        db.DriverProfiles.Add(new DriverProfile { UserId = driver.Id, IsOnline = true, KycStatus = KycStatus.Approved, OnboardingComplete = true });
        var trip = new Trip
        {
            RiderId = rider.Id,
            DriverId = driver.Id,
            Code = "ZR1",
            Market = "ci",
            PickupLabel = "A",
            DropoffLabel = "B",
            JobState = JobState.PickedUp
        };
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var handler = new ReportTripHandler(db, FakeServices.Realtime(), FakeServices.Email(), FakeServices.Push());
        for (var i = 0; i < AccountModeration.AutoSuspendAfter; i++)
        {
            db.AccountReports.Add(new AccountReport
            {
                ReporterUserId = $"other-{i}",
                ReportedUserId = driver.Id,
                Category = "safety",
                Description = "earlier report",
                TicketId = $"tkt-{i}"
            });
        }
        await db.SaveChangesAsync();

        await handler.Handle(new ReportTripCommand(
            rider.Id, "rider", trip.Id,
            new ReportTripRequest("safety", "Driver refused the route", null)), CancellationToken.None);

        var updated = db.Users.Find(driver.Id)!;
        updated.Status.Should().Be(UserStatus.Suspended);
        updated.SuspensionReason.Should().Contain("4");
        db.DriverProfiles.Single(p => p.UserId == driver.Id).IsOnline.Should().BeFalse();
        db.Trips.Find(trip.Id)!.JobState.Should().Be(JobState.CancelledByDriver);
        db.SupportTickets.Should().Contain(t => t.UserId == driver.Id && t.Category == "appeal");
        db.AccountReports.Count(r => r.ReportedUserId == driver.Id).Should().Be(4);
    }

    [Fact]
    public async Task ThirdReport_DoesNotSuspend()
    {
        using var db = DbContextFactory.Create();
        var rider = new User { Phone = "+2250701000003", Role = UserRole.Rider, Status = UserStatus.Active };
        var driver = new User { Phone = "+2250701000004", Role = UserRole.Driver, Status = UserStatus.Active };
        db.Users.AddRange(rider, driver);
        var trip = new Trip
        {
            RiderId = rider.Id, DriverId = driver.Id, Code = "ZR2", Market = "ci",
            PickupLabel = "A", DropoffLabel = "B"
        };
        db.Trips.Add(trip);
        db.AccountReports.Add(new AccountReport { ReporterUserId = "a", ReportedUserId = driver.Id, Category = "safety", Description = "1", TicketId = "t1" });
        db.AccountReports.Add(new AccountReport { ReporterUserId = "b", ReportedUserId = driver.Id, Category = "safety", Description = "2", TicketId = "t2" });
        await db.SaveChangesAsync();

        var handler = new ReportTripHandler(db, FakeServices.Realtime(), FakeServices.Email(), FakeServices.Push());
        await handler.Handle(new ReportTripCommand(
            rider.Id, "rider", trip.Id,
            new ReportTripRequest("safety", "Late arrival", null)), CancellationToken.None);

        db.Users.Find(driver.Id)!.Status.Should().Be(UserStatus.Active);
        db.AccountReports.Count(r => r.ReportedUserId == driver.Id).Should().Be(3);
    }

    [Fact]
    public async Task SuspendedUser_CanPostAnAppeal()
    {
        using var db = DbContextFactory.Create();
        var driver = new User
        {
            Phone = "+2250701000005", Role = UserRole.Driver,
            Status = UserStatus.Suspended, SuspensionReason = "Reports"
        };
        db.Users.Add(driver);
        await db.SaveChangesAsync();

        var handler = new PostAppealHandler(db, FakeServices.Realtime(), FakeServices.Email());
        var appeal = await handler.Handle(new PostAppealCommand(driver.Id, "I followed the route."), CancellationToken.None);

        appeal.Locked.Should().BeTrue();
        appeal.Messages.Should().Contain(m => m.Sender == "you" && m.Body.Contains("route"));
        db.SupportTickets.Should().Contain(t => t.UserId == driver.Id && t.Category == "appeal");
    }
}
