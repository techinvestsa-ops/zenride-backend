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
        var handler = new ApproveKycStepHandler(db, FakeServices.Audit());

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

        var handler = new ApproveKycStepHandler(db, FakeServices.Audit());
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

        var handler = new ApproveKycStepHandler(db, FakeServices.Audit());
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
        var handler = new RejectKycStepHandler(db, FakeServices.Audit(), FakeServices.Realtime());

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

        var handler = new RejectKycStepHandler(db, FakeServices.Audit(), FakeServices.Realtime());
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
        var handler = new ApproveKycApplicationHandler(db, FakeServices.Audit(), FakeServices.Realtime());

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

        var handler = new ApproveKycApplicationHandler(db, FakeServices.Audit(), FakeServices.Realtime());
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

        var handler = new ApproveKycApplicationHandler(db, FakeServices.Audit(), FakeServices.Realtime());
        var result  = await handler.Handle(
            new ApproveKycApplicationCommand(dp.Id, "staff1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        db.DriverProfiles.Find(dp.Id)!.KycStatus.Should().Be(KycStatus.Approved);
        db.DriverProfiles.Find(dp.Id)!.OnboardingComplete.Should().BeTrue();
    }

    // ── RejectKycApplicationHandler ───────────────────────────────────────────

    [Fact]
    public async Task RejectKycApplication_DriverNotFound_ReturnsFalse()
    {
        using var db = DbContextFactory.Create();
        var handler = new RejectKycApplicationHandler(db, FakeServices.Audit(), FakeServices.Realtime());

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

        var handler = new RejectKycApplicationHandler(db, FakeServices.Audit(), FakeServices.Realtime());
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

        var handler = new RejectKycApplicationHandler(db, FakeServices.Audit(), FakeServices.Realtime());
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
        var handler = new AssignKycReviewerHandler(db, FakeServices.Audit());

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
        db.DriverOnboardings.Add(onb);
        await db.SaveChangesAsync();

        var handler = new AssignKycReviewerHandler(db, FakeServices.Audit());
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

        var handler = new AssignKycReviewerHandler(db, FakeServices.Audit());
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

        var handler = new AssignKycReviewerHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new AssignKycReviewerCommand(dp.Id, "staff1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeTrue(); // re-assigning self is allowed
    }
}
