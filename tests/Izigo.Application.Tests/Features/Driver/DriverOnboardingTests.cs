using FluentAssertions;
using Izigo.Application.Features.Driver.Commands;
using Izigo.Application.Features.Driver.Dtos;
using Izigo.Application.Features.Driver.Queries;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Enums;
using Xunit;
using DriverProfileEntity = Izigo.Domain.Entities.DriverProfile;
using DriverOnboardingEntity = Izigo.Domain.Entities.DriverOnboarding;
using VehicleEntity = Izigo.Domain.Entities.Vehicle;
using PayoutMethodEntity = Izigo.Domain.Entities.PayoutMethod;

namespace Izigo.Application.Tests.Features.Driver;

public class DriverOnboardingTests
{
    private static DriverProfileEntity BuildDriverProfile(string userId) =>
        new() { UserId = userId };

    private static DriverOnboardingEntity BuildFullOnboarding(string driverId,
        OnboardingStepStatus stepStatus = OnboardingStepStatus.Submitted) => new()
    {
        DriverId        = driverId,
        PersonalStatus  = stepStatus,
        IdentityStatus  = stepStatus,
        LicenseStatus   = stepStatus,
        VehicleStatus   = stepStatus,
        InsuranceStatus = stepStatus,
        GuarantorStatus = stepStatus,
        PayoutStatus    = stepStatus,
        SelfieStatus    = stepStatus,
        PersonalDataJson  = "{}",
        IdentityDataJson  = "{}",
        LicenseDataJson   = "{}",
        VehicleDataJson   = "{}",
        InsuranceDataJson = "{}",
        GuarantorDataJson = "{}",
        PayoutDataJson    = "{}"
    };

    // ── SubmitPersonalHandler ─────────────────────────────────────────────────

    [Fact]
    public async Task SubmitPersonal_CreatesOnboardingIfMissing_AndSetsPersonalSubmitted()
    {
        using var db = DbContextFactory.Create();
        var driverId = Guid.NewGuid().ToString();
        var dp = BuildDriverProfile(driverId);
        db.DriverProfiles.Add(dp);
        await db.SaveChangesAsync();

        var handler = new SubmitPersonalHandler(db);
        var result  = await handler.Handle(
            new SubmitPersonalCommand(driverId, new SubmitPersonalRequest(
                "John", "Doe", DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-25)),
                "male", "123 Street", "Abidjan", null, null, null)),
            CancellationToken.None);

        result.Should().NotBeNull();
        var onb = db.DriverOnboardings.First(o => o.DriverId == driverId);
        onb.PersonalStatus.Should().Be(OnboardingStepStatus.Submitted);
    }

    // ── SubmitVehicleHandler ──────────────────────────────────────────────────

    [Fact]
    public async Task SubmitVehicle_InvalidVehicleType_ThrowsArgumentException()
    {
        using var db = DbContextFactory.Create();
        var driverId = Guid.NewGuid().ToString();
        var dp = BuildDriverProfile(driverId);
        db.DriverProfiles.Add(dp);
        db.DriverOnboardings.Add(BuildFullOnboarding(driverId));
        await db.SaveChangesAsync();

        var handler = new SubmitVehicleHandler(db);
        var act = () => handler.Handle(
            new SubmitVehicleCommand(driverId, new SubmitVehicleRequest(
                "Toyota", "Corolla", 2020, "White", "AB-1234-CI", "INVALID_TYPE", 4)),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*vehicle_type*");
    }

    [Fact]
    public async Task SubmitVehicle_ValidType_CreatesVehicleAndSetsStatus()
    {
        using var db = DbContextFactory.Create();
        var driverId = Guid.NewGuid().ToString();
        var dp = BuildDriverProfile(driverId);
        db.DriverProfiles.Add(dp);
        db.DriverOnboardings.Add(BuildFullOnboarding(driverId));
        await db.SaveChangesAsync();

        var handler = new SubmitVehicleHandler(db);
        var result  = await handler.Handle(
            new SubmitVehicleCommand(driverId, new SubmitVehicleRequest(
                "Toyota", "Corolla", 2020, "White", "AB-1234-CI", "Car", 4)),
            CancellationToken.None);

        result.Should().NotBeNull();
        var onb = db.DriverOnboardings.First(o => o.DriverId == driverId);
        onb.VehicleStatus.Should().Be(OnboardingStepStatus.Submitted);
        db.Vehicles.Should().HaveCount(1);
        db.Vehicles.First().PendingReview.Should().BeTrue();
    }

    [Fact]
    public async Task SubmitVehicle_ExistingPendingVehicle_UpdatesRatherThanCreatesNew()
    {
        using var db = DbContextFactory.Create();
        var driverId = Guid.NewGuid().ToString();
        var dp = BuildDriverProfile(driverId);
        db.DriverProfiles.Add(dp);
        db.DriverOnboardings.Add(BuildFullOnboarding(driverId));
        db.Vehicles.Add(new VehicleEntity
        {
            DriverId = driverId, Make = "Honda", Model = "Civic", Year = 2018,
            Color = "Red", Plate = "OLD-PLATE", Type = VehicleType.Car,
            Seats = 4, PendingReview = true
        });
        await db.SaveChangesAsync();

        var handler = new SubmitVehicleHandler(db);
        await handler.Handle(
            new SubmitVehicleCommand(driverId, new SubmitVehicleRequest(
                "Toyota", "Corolla", 2022, "White", "NEW-PLATE", "Car", 4)),
            CancellationToken.None);

        db.Vehicles.Should().HaveCount(1);
        db.Vehicles.First().Make.Should().Be("Toyota");
        db.Vehicles.First().Plate.Should().Be("NEW-PLATE");
    }

    // ── SubmitPayoutHandler ───────────────────────────────────────────────────

    [Fact]
    public async Task SubmitPayout_NewPayout_CreatesPayoutMethodAndSetsStatus()
    {
        using var db = DbContextFactory.Create();
        var driverId = Guid.NewGuid().ToString();
        var dp = BuildDriverProfile(driverId);
        db.DriverProfiles.Add(dp);
        db.DriverOnboardings.Add(BuildFullOnboarding(driverId));
        await db.SaveChangesAsync();

        var handler = new SubmitPayoutHandler(db);
        var result  = await handler.Handle(
            new SubmitPayoutCommand(driverId, new SubmitPayoutRequest(
                "mobile_money", null, null, null, "+2250700000099")),
            CancellationToken.None);

        result.Should().NotBeNull();
        var onb = db.DriverOnboardings.First(o => o.DriverId == driverId);
        onb.PayoutStatus.Should().Be(OnboardingStepStatus.Submitted);
        db.PayoutMethods.Should().HaveCount(1);
        db.PayoutMethods.First().IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task SubmitPayout_ExistingDefault_UpdatesRatherThanCreatesNew()
    {
        using var db = DbContextFactory.Create();
        var driverId = Guid.NewGuid().ToString();
        var dp = BuildDriverProfile(driverId);
        db.DriverProfiles.Add(dp);
        db.DriverOnboardings.Add(BuildFullOnboarding(driverId));
        db.PayoutMethods.Add(new PayoutMethodEntity
        {
            DriverId = driverId, Method = "bank_transfer",
            AccountNumber = "0001234567", IsDefault = true
        });
        await db.SaveChangesAsync();

        var handler = new SubmitPayoutHandler(db);
        await handler.Handle(
            new SubmitPayoutCommand(driverId, new SubmitPayoutRequest(
                "mobile_money", null, null, null, "+2250700000099")),
            CancellationToken.None);

        db.PayoutMethods.Should().HaveCount(1);
        db.PayoutMethods.First().Method.Should().Be("mobile_money");
    }

    // ── SubmitOnboardingHandler ───────────────────────────────────────────────

    [Fact]
    public async Task SubmitOnboarding_StepsIncomplete_ThrowsConflict()
    {
        using var db = DbContextFactory.Create();
        var driverId = Guid.NewGuid().ToString();
        var dp = BuildDriverProfile(driverId);
        db.DriverProfiles.Add(dp);
        // PersonalStatus left as Empty
        db.DriverOnboardings.Add(BuildFullOnboarding(driverId, OnboardingStepStatus.Empty));
        await db.SaveChangesAsync();

        var handler = new SubmitOnboardingHandler(db);
        var act = () => handler.Handle(
            new SubmitOnboardingCommand(driverId), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*steps must be submitted*");
    }

    [Fact]
    public async Task SubmitOnboarding_AlreadySubmitted_ThrowsConflict()
    {
        using var db = DbContextFactory.Create();
        var driverId = Guid.NewGuid().ToString();
        var dp = BuildDriverProfile(driverId);
        db.DriverProfiles.Add(dp);
        var onb = BuildFullOnboarding(driverId);
        onb.SubmittedAt = DateTime.UtcNow.AddHours(-1);
        db.DriverOnboardings.Add(onb);
        await db.SaveChangesAsync();

        var handler = new SubmitOnboardingHandler(db);
        var act = () => handler.Handle(
            new SubmitOnboardingCommand(driverId), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already been submitted*");
    }

    [Fact]
    public async Task SubmitOnboarding_AllStepsComplete_SetsKycToInReview()
    {
        using var db = DbContextFactory.Create();
        var driverId = Guid.NewGuid().ToString();
        var dp = BuildDriverProfile(driverId);
        db.DriverProfiles.Add(dp);
        db.DriverOnboardings.Add(BuildFullOnboarding(driverId));
        await db.SaveChangesAsync();

        var handler = new SubmitOnboardingHandler(db);
        var result  = await handler.Handle(
            new SubmitOnboardingCommand(driverId), CancellationToken.None);

        result.KycStatus.Should().Be("inreview");
        result.SubmittedAt.Should().NotBeNull();

        var profile = db.DriverProfiles.First(d => d.UserId == driverId);
        profile.KycStatus.Should().Be(KycStatus.InReview);
    }
}
