using FluentAssertions;
using Izigo.Application.Features.Admin.Kyc.Commands;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Enums;
using Xunit;
using DriverProfileEntity = Izigo.Domain.Entities.DriverProfile;
using DriverOnboardingEntity = Izigo.Domain.Entities.DriverOnboarding;
using UserEntity = Izigo.Domain.Entities.User;

namespace Izigo.Application.Tests.Features.Admin.Kyc;

public class AdminKycHandlerTests
{
    /// <summary>
    /// Onboarding.DriverId = DriverProfile.Id (the entity Id, not UserId).
    /// KYC commands receive the DriverProfile.Id as their DriverId parameter.
    /// </summary>
    private static DriverOnboardingEntity BuildFullOnboarding(string driverProfileId,
        OnboardingStepStatus status = OnboardingStepStatus.Submitted) => new()
    {
        DriverId        = driverProfileId,
        PersonalStatus  = status,
        IdentityStatus  = status,
        LicenseStatus   = status,
        VehicleStatus   = status,
        InsuranceStatus = status,
        GuarantorStatus = status,
        PayoutStatus    = status,
        SelfieStatus    = status,
        SubmittedAt     = DateTime.UtcNow.AddHours(-2)
    };

    // ── ApproveKycStepHandler ─────────────────────────────────────────────────

    [Fact]
    public async Task ApproveKycStep_ApplicationNotFound_ReturnsFalse()
    {
        using var db = DbContextFactory.Create();
        var handler = new ApproveKycStepHandler(db, FakeServices.Audit(), FakeServices.Realtime());

        var result = await handler.Handle(
            new ApproveKycStepCommand("nonexistent", "identity", "staff1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("APPLICATION_NOT_FOUND");
    }

    [Fact]
    public async Task ApproveKycStep_InvalidStep_ReturnsFalse()
    {
        using var db = DbContextFactory.Create();
        var dp = new DriverProfileEntity { UserId = Guid.NewGuid().ToString() };
        db.DriverProfiles.Add(dp);
        db.DriverOnboardings.Add(BuildFullOnboarding(dp.Id));
        await db.SaveChangesAsync();

        var handler = new ApproveKycStepHandler(db, FakeServices.Audit(), FakeServices.Realtime());
        var result  = await handler.Handle(
            new ApproveKycStepCommand(dp.Id, "invalid_step", "staff1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_STEP");
    }

    [Theory]
    [InlineData("personal")]
    [InlineData("identity")]
    [InlineData("license")]
    [InlineData("vehicle")]
    [InlineData("insurance")]
    [InlineData("guarantor")]
    [InlineData("payout")]
    [InlineData("selfie")]
    public async Task ApproveKycStep_ValidStep_SetsStepToApproved(string step)
    {
        using var db = DbContextFactory.Create();
        var dp = new DriverProfileEntity { UserId = Guid.NewGuid().ToString() };
        db.DriverProfiles.Add(dp);
        db.DriverOnboardings.Add(BuildFullOnboarding(dp.Id));
        await db.SaveChangesAsync();

        var handler = new ApproveKycStepHandler(db, FakeServices.Audit(), FakeServices.Realtime());
        var result  = await handler.Handle(
            new ApproveKycStepCommand(dp.Id, step, "staff1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var onb = db.DriverOnboardings.First(o => o.DriverId == dp.Id);
        var stepStatus = step switch
        {
            "personal"  => onb.PersonalStatus,
            "identity"  => onb.IdentityStatus,
            "license"   => onb.LicenseStatus,
            "vehicle"   => onb.VehicleStatus,
            "insurance" => onb.InsuranceStatus,
            "guarantor" => onb.GuarantorStatus,
            "payout"    => onb.PayoutStatus,
            "selfie"    => onb.SelfieStatus,
            _           => OnboardingStepStatus.Empty
        };
        stepStatus.Should().Be(OnboardingStepStatus.Approved);
    }

    // ── RejectKycStepHandler ──────────────────────────────────────────────────

    [Fact]
    public async Task RejectKycStep_ApplicationNotFound_ReturnsFalse()
    {
        using var db = DbContextFactory.Create();
        var handler = new RejectKycStepHandler(db, FakeServices.Audit(), FakeServices.Realtime(), FakeServices.Push());

        var result = await handler.Handle(
            new RejectKycStepCommand("nonexistent", "identity", "Invalid ID", "staff1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("APPLICATION_NOT_FOUND");
    }

    [Fact]
    public async Task RejectKycStep_ValidStep_SetsStepToRejected()
    {
        using var db = DbContextFactory.Create();
        var dp = new DriverProfileEntity { UserId = Guid.NewGuid().ToString() };
        db.DriverProfiles.Add(dp);
        db.DriverOnboardings.Add(BuildFullOnboarding(dp.Id));
        await db.SaveChangesAsync();

        var handler = new RejectKycStepHandler(db, FakeServices.Audit(), FakeServices.Realtime(), FakeServices.Push());
        var result  = await handler.Handle(
            new RejectKycStepCommand(dp.Id, "identity", "ID image is blurry", "staff1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var onb = db.DriverOnboardings.First(o => o.DriverId == dp.Id);
        onb.IdentityStatus.Should().Be(OnboardingStepStatus.Rejected);
        onb.IdentityRejectionReason.Should().Be("ID image is blurry");
    }

    // ── ApproveKycApplicationHandler ──────────────────────────────────────────

    [Fact]
    public async Task ApproveKycApplication_ApplicationNotFound_ReturnsFalse()
    {
        using var db = DbContextFactory.Create();
        var handler = new ApproveKycApplicationHandler(db, FakeServices.Audit(), FakeServices.Realtime(), FakeServices.Push());

        var result = await handler.Handle(
            new ApproveKycApplicationCommand("nonexistent", "staff1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("APPLICATION_NOT_FOUND");
    }

    [Fact]
    public async Task ApproveKycApplication_StepsNotApproved_ReturnsFalse()
    {
        using var db = DbContextFactory.Create();
        var dp = new DriverProfileEntity { UserId = Guid.NewGuid().ToString() };
        db.DriverProfiles.Add(dp);
        // Only personal step approved; rest are Submitted
        var onb = BuildFullOnboarding(dp.Id, OnboardingStepStatus.Submitted);
        onb.PersonalStatus = OnboardingStepStatus.Approved;
        db.DriverOnboardings.Add(onb);
        await db.SaveChangesAsync();

        var handler = new ApproveKycApplicationHandler(db, FakeServices.Audit(), FakeServices.Realtime(), FakeServices.Push());
        var result  = await handler.Handle(
            new ApproveKycApplicationCommand(dp.Id, "staff1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().StartWith("STEPS_NOT_APPROVED");
    }

    [Fact]
    public async Task ApproveKycApplication_AllStepsApproved_SetsKycApproved()
    {
        using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid().ToString();
        // DriverProfile.Id is auto-generated; that's the KYC driverId
        var dp = new DriverProfileEntity { UserId = userId };
        db.DriverProfiles.Add(dp);
        db.DriverOnboardings.Add(BuildFullOnboarding(dp.Id, OnboardingStepStatus.Approved));
        await db.SaveChangesAsync();

        var handler = new ApproveKycApplicationHandler(db, FakeServices.Audit(), FakeServices.Realtime(), FakeServices.Push());
        var result  = await handler.Handle(
            new ApproveKycApplicationCommand(dp.Id, "staff1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        db.DriverProfiles.Find(dp.Id)!.KycStatus.Should().Be(KycStatus.Approved);
        db.DriverProfiles.Find(dp.Id)!.OnboardingComplete.Should().BeTrue();
    }

    [Fact]
    public async Task ApproveKycApplication_ByUserId_ActivatesVehicleAndEnablesVerticals()
    {
        using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid().ToString();
        var dp = new DriverProfileEntity { UserId = userId };
        db.DriverProfiles.Add(dp);
        db.DriverOnboardings.Add(BuildFullOnboarding(dp.Id, OnboardingStepStatus.Approved));
        db.Vehicles.Add(new Izigo.Domain.Entities.Vehicle
        {
            DriverId = userId, Make = "Toyota", Model = "Corolla", Plate = "AB-1",
            Type = VehicleType.Car, Seats = 4, PendingReview = true,
        });
        await db.SaveChangesAsync();

        var handler = new ApproveKycApplicationHandler(db, FakeServices.Audit(), FakeServices.Realtime(), FakeServices.Push());
        var result  = await handler.Handle(
            new ApproveKycApplicationCommand(userId, "staff1", "Admin"), CancellationToken.None);

        result.Success.Should().BeTrue();
        var vehicle = db.Vehicles.Single();
        vehicle.IsActive.Should().BeTrue();
        vehicle.PendingReview.Should().BeFalse();
        db.DriverProfiles.Find(dp.Id)!.VerticalsAllowed
            .Should().BeEquivalentTo([Vertical.Ride, Vertical.CoRide, Vertical.Package]);
    }

    // ── RejectKycApplicationHandler ───────────────────────────────────────────

    [Fact]
    public async Task RejectKycApplication_DriverNotFound_ReturnsFalse()
    {
        using var db = DbContextFactory.Create();
        var handler = new RejectKycApplicationHandler(db, FakeServices.Audit(), FakeServices.Realtime(), FakeServices.Push());

        var result = await handler.Handle(
            new RejectKycApplicationCommand("nonexistent", "Fraud detected", false, "staff1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("DRIVER_NOT_FOUND");
    }

    [Fact]
    public async Task RejectKycApplication_Blocklist_BlocksUser()
    {
        using var db = DbContextFactory.Create();
        var user = new UserEntity
        {
            FirstName = "Driver", LastName = "Fraud",
            Phone = "+2250700000099", Role = UserRole.Driver,
            Rating = 5.0m, Status = UserStatus.Active
        };
        db.Users.Add(user);
        var dp = new DriverProfileEntity { UserId = user.Id };
        db.DriverProfiles.Add(dp);
        await db.SaveChangesAsync();

        var handler = new RejectKycApplicationHandler(db, FakeServices.Audit(), FakeServices.Realtime(), FakeServices.Push());
        var result  = await handler.Handle(
            new RejectKycApplicationCommand(dp.Id, "Fraudulent documents", true, "staff1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        db.DriverProfiles.Find(dp.Id)!.KycStatus.Should().Be(KycStatus.Rejected);
        db.Users.Find(user.Id)!.Status.Should().Be(UserStatus.Blocked);
    }

    [Fact]
    public async Task RejectKycApplication_NoBlocklist_OnlyRejectsKyc()
    {
        using var db = DbContextFactory.Create();
        var user = new UserEntity
        {
            FirstName = "Driver", LastName = "Test",
            Phone = "+2250700000088", Role = UserRole.Driver,
            Rating = 5.0m, Status = UserStatus.Active
        };
        db.Users.Add(user);
        var dp = new DriverProfileEntity { UserId = user.Id };
        db.DriverProfiles.Add(dp);
        await db.SaveChangesAsync();

        var handler = new RejectKycApplicationHandler(db, FakeServices.Audit(), FakeServices.Realtime(), FakeServices.Push());
        var result  = await handler.Handle(
            new RejectKycApplicationCommand(dp.Id, "Incomplete documents", false, "staff1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        db.DriverProfiles.Find(dp.Id)!.KycStatus.Should().Be(KycStatus.Rejected);
        db.Users.Find(user.Id)!.Status.Should().Be(UserStatus.Active); // not blocked
    }

    // ── AssignKycReviewerHandler ──────────────────────────────────────────────

    [Fact]
    public async Task AssignKycReviewer_ApplicationNotFound_ReturnsFalse()
    {
        using var db = DbContextFactory.Create();
        var handler = new AssignKycReviewerHandler(db, FakeServices.Realtime());

        var result = await handler.Handle(
            new AssignKycReviewerCommand("nonexistent", "staff1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("APPLICATION_NOT_FOUND");
    }

    [Fact]
    public async Task AssignKycReviewer_AlreadyAssignedToOther_ReturnsFalse()
    {
        using var db = DbContextFactory.Create();
        var dp = new DriverProfileEntity { UserId = Guid.NewGuid().ToString() };
        db.DriverProfiles.Add(dp);
        var onb = BuildFullOnboarding(dp.Id);
        onb.AssignedReviewerStaffId = "existing-reviewer";
        onb.AssignedAt              = DateTime.UtcNow.AddMinutes(-5);
        db.DriverOnboardings.Add(onb);
        await db.SaveChangesAsync();

        var handler = new AssignKycReviewerHandler(db, FakeServices.Realtime());
        var result  = await handler.Handle(
            new AssignKycReviewerCommand(dp.Id, "different-reviewer", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("ALREADY_ASSIGNED");
    }

    [Fact]
    public async Task AssignKycReviewer_Success_SetsReviewer()
    {
        using var db = DbContextFactory.Create();
        var dp = new DriverProfileEntity { UserId = Guid.NewGuid().ToString() };
        db.DriverProfiles.Add(dp);
        db.DriverOnboardings.Add(BuildFullOnboarding(dp.Id));
        await db.SaveChangesAsync();

        var handler = new AssignKycReviewerHandler(db, FakeServices.Realtime());
        var result  = await handler.Handle(
            new AssignKycReviewerCommand(dp.Id, "staff1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        db.DriverOnboardings.First(o => o.DriverId == dp.Id)
            .AssignedReviewerStaffId.Should().Be("staff1");
    }

    [Fact]
    public async Task AssignKycReviewer_SameReviewer_Idempotent()
    {
        using var db = DbContextFactory.Create();
        var dp = new DriverProfileEntity { UserId = Guid.NewGuid().ToString() };
        db.DriverProfiles.Add(dp);
        var onb = BuildFullOnboarding(dp.Id);
        onb.AssignedReviewerStaffId = "staff1";
        db.DriverOnboardings.Add(onb);
        await db.SaveChangesAsync();

        var handler = new AssignKycReviewerHandler(db, FakeServices.Realtime());
        var result  = await handler.Handle(
            new AssignKycReviewerCommand(dp.Id, "staff1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeTrue(); // re-assigning self is allowed
    }

    // ── Review claims ────────────────────────────────────────────────────────

    private static async Task<(Izigo.Infrastructure.Persistence.ApplicationDbContext Db, DriverProfileEntity Dp, DriverOnboardingEntity Onb)>
        ClaimedApplication(string? holder, int claimedMinutesAgo = 5)
    {
        var db = DbContextFactory.Create();
        var dp = new DriverProfileEntity { UserId = Guid.NewGuid().ToString() };
        db.DriverProfiles.Add(dp);
        var onb = BuildFullOnboarding(dp.Id);
        onb.AssignedReviewerStaffId = holder;
        onb.AssignedAt = holder is null ? null : DateTime.UtcNow.AddMinutes(-claimedMinutesAgo);
        db.DriverOnboardings.Add(onb);
        await db.SaveChangesAsync();
        return (db, dp, onb);
    }

    [Fact]
    public async Task AssignKycReviewer_ExpiredClaim_CanBeTakenOver()
    {
        var (db, dp, onb) = await ClaimedApplication("away-reviewer", claimedMinutesAgo: 45);
        using var _ = db;

        var result = await new AssignKycReviewerHandler(db, FakeServices.Realtime()).Handle(
            new AssignKycReviewerCommand(dp.Id, "staff2", "Admin"), CancellationToken.None);

        result.Success.Should().BeTrue();
        onb.AssignedReviewerStaffId.Should().Be("staff2");
    }

    [Fact]
    public async Task ApproveKycStep_ClaimedByAnotherReviewer_IsRefused()
    {
        var (db, dp, onb) = await ClaimedApplication("staff1");
        using var _ = db;

        var result = await new ApproveKycStepHandler(db, FakeServices.Audit(), FakeServices.Realtime()).Handle(
            new ApproveKycStepCommand(dp.Id, "personal", "staff2", "Other"), CancellationToken.None);

        result.ErrorCode.Should().Be("CLAIMED_BY_OTHER");
        onb.PersonalStatus.Should().Be(OnboardingStepStatus.Submitted);
    }

    [Fact]
    public async Task ApproveKycStep_Unclaimed_ClaimsForTheReviewer()
    {
        var (db, dp, onb) = await ClaimedApplication(null);
        using var _ = db;

        await new ApproveKycStepHandler(db, FakeServices.Audit(), FakeServices.Realtime()).Handle(
            new ApproveKycStepCommand(dp.Id, "personal", "staff1", "Admin"), CancellationToken.None);

        onb.AssignedReviewerStaffId.Should().Be("staff1");
        onb.AssignedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RejectKycApplication_StoresReason_AndReleasesTheClaim()
    {
        var (db, dp, onb) = await ClaimedApplication("staff1");
        using var _ = db;

        var result = await new RejectKycApplicationHandler(db, FakeServices.Audit(), FakeServices.Realtime(), FakeServices.Push())
            .Handle(new RejectKycApplicationCommand(dp.Id, "Photos are unreadable", false, "staff1", "Admin"),
                CancellationToken.None);

        result.Success.Should().BeTrue();
        onb.ApplicationRejectionReason.Should().Be("Photos are unreadable");
        onb.SubmittedAt.Should().BeNull();
        onb.AssignedReviewerStaffId.Should().BeNull();
    }

    [Fact]
    public async Task ReleaseKycReviewer_ByTheHolder_ClearsTheClaim()
    {
        var (db, dp, onb) = await ClaimedApplication("staff1");
        using var _ = db;

        await new ReleaseKycReviewerHandler(db, FakeServices.Realtime())
            .Handle(new ReleaseKycReviewerCommand(dp.Id, "staff1"), CancellationToken.None);

        onb.AssignedReviewerStaffId.Should().BeNull();
        onb.AssignedAt.Should().BeNull();
    }

    [Fact]
    public async Task ReleaseKycReviewer_BySomeoneElse_KeepsTheClaim()
    {
        var (db, dp, onb) = await ClaimedApplication("staff1");
        using var _ = db;

        await new ReleaseKycReviewerHandler(db, FakeServices.Realtime())
            .Handle(new ReleaseKycReviewerCommand(dp.Id, "staff2"), CancellationToken.None);

        onb.AssignedReviewerStaffId.Should().Be("staff1");
    }
}
