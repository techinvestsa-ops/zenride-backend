using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Driver.Dtos;
using Izigo.Application.Features.Driver.Queries;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Driver.Commands;

// ── POST /driver/onboarding/personal ─────────────────────────────────────────

public record SubmitPersonalCommand(string DriverId, SubmitPersonalRequest Request)
    : IRequest<OnboardingStatusDto>;

public class SubmitPersonalHandler(IApplicationDbContext db)
    : IRequestHandler<SubmitPersonalCommand, OnboardingStatusDto>
{
    public async Task<OnboardingStatusDto> Handle(SubmitPersonalCommand cmd, CancellationToken ct)
    {
        var onb = await OnboardingHelper.GetOrCreateAsync(cmd.DriverId, db, ct);
        var req = cmd.Request;

        // Update user name fields
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == cmd.DriverId, ct);
        if (user != null)
        {
            user.FirstName    = req.FirstName;
            user.LastName     = req.LastName;
            user.DateOfBirth  = req.DateOfBirth;
            user.Gender       = req.Gender;
        }

        onb.PersonalDataJson = System.Text.Json.JsonSerializer.Serialize(req);
        onb.PersonalStatus   = OnboardingStepStatus.Submitted;

        await db.SaveChangesAsync(ct);
        return await OnboardingShared.BuildStatusAsync(cmd.DriverId, onb, db, ct);
    }
}

// ── POST /driver/onboarding/identity ─────────────────────────────────────────

public record SubmitIdentityCommand(string DriverId, SubmitIdentityRequest Request)
    : IRequest<OnboardingStatusDto>;

public class SubmitIdentityHandler(IApplicationDbContext db)
    : IRequestHandler<SubmitIdentityCommand, OnboardingStatusDto>
{
    public async Task<OnboardingStatusDto> Handle(SubmitIdentityCommand cmd, CancellationToken ct)
    {
        var onb = await OnboardingHelper.GetOrCreateAsync(cmd.DriverId, db, ct);
        var req = cmd.Request;

        OnboardingShared.UpsertDocument(db, cmd.DriverId, DocumentType.GovernmentId, req.FileUrl, req.BackFileUrl,
            req.ExpiryDate.HasValue ? req.ExpiryDate.Value.ToDateTime(TimeOnly.MinValue) : null);

        onb.IdentityDataJson = System.Text.Json.JsonSerializer.Serialize(req);
        onb.IdentityStatus   = OnboardingStepStatus.Submitted;

        await db.SaveChangesAsync(ct);
        return await OnboardingShared.BuildStatusAsync(cmd.DriverId, onb, db, ct);
    }
}

// ── POST /driver/onboarding/license ──────────────────────────────────────────

public record SubmitLicenseCommand(string DriverId, SubmitLicenseRequest Request)
    : IRequest<OnboardingStatusDto>;

public class SubmitLicenseHandler(IApplicationDbContext db)
    : IRequestHandler<SubmitLicenseCommand, OnboardingStatusDto>
{
    public async Task<OnboardingStatusDto> Handle(SubmitLicenseCommand cmd, CancellationToken ct)
    {
        var onb = await OnboardingHelper.GetOrCreateAsync(cmd.DriverId, db, ct);
        var req = cmd.Request;

        OnboardingShared.UpsertDocument(db, cmd.DriverId, DocumentType.DriversLicense, req.FileUrl, null,
            req.ExpiryDate.ToDateTime(TimeOnly.MinValue));

        onb.LicenseDataJson = System.Text.Json.JsonSerializer.Serialize(req);
        onb.LicenseStatus   = OnboardingStepStatus.Submitted;

        await db.SaveChangesAsync(ct);
        return await OnboardingShared.BuildStatusAsync(cmd.DriverId, onb, db, ct);
    }
}

// ── POST /driver/onboarding/vehicle ──────────────────────────────────────────

public record SubmitVehicleCommand(string DriverId, SubmitVehicleRequest Request)
    : IRequest<OnboardingStatusDto>;

public class SubmitVehicleHandler(IApplicationDbContext db)
    : IRequestHandler<SubmitVehicleCommand, OnboardingStatusDto>
{
    public async Task<OnboardingStatusDto> Handle(SubmitVehicleCommand cmd, CancellationToken ct)
    {
        var onb = await OnboardingHelper.GetOrCreateAsync(cmd.DriverId, db, ct);
        var req = cmd.Request;

        if (!Enum.TryParse<VehicleType>(req.VehicleType, true, out var vt))
            throw new ArgumentException("VALIDATION_ERROR: Invalid vehicle_type.");

        // Upsert onboarding vehicle (mark pending review)
        var existing = await db.Vehicles
            .Where(v => v.DriverId == cmd.DriverId && v.PendingReview)
            .FirstOrDefaultAsync(ct);

        if (existing != null)
        {
            existing.Make  = req.Make;
            existing.Model = req.Model;
            existing.Year  = req.Year;
            existing.Color = req.Color;
            existing.Plate = req.Plate;
            existing.Type  = vt;
            existing.Seats = req.Seats;
        }
        else
        {
            db.Vehicles.Add(new Vehicle
            {
                DriverId      = cmd.DriverId,
                Make          = req.Make,
                Model         = req.Model,
                Year          = req.Year,
                Color         = req.Color,
                Plate         = req.Plate,
                Type          = vt,
                Seats         = req.Seats,
                IsActive      = false,
                PendingReview = true,
            });
        }

        onb.VehicleDataJson = System.Text.Json.JsonSerializer.Serialize(req);
        onb.VehicleStatus   = OnboardingStepStatus.Submitted;

        await db.SaveChangesAsync(ct);
        return await OnboardingShared.BuildStatusAsync(cmd.DriverId, onb, db, ct);
    }
}

// ── POST /driver/onboarding/insurance ────────────────────────────────────────

public record SubmitInsuranceCommand(string DriverId, SubmitInsuranceRequest Request)
    : IRequest<OnboardingStatusDto>;

public class SubmitInsuranceHandler(IApplicationDbContext db)
    : IRequestHandler<SubmitInsuranceCommand, OnboardingStatusDto>
{
    public async Task<OnboardingStatusDto> Handle(SubmitInsuranceCommand cmd, CancellationToken ct)
    {
        var onb = await OnboardingHelper.GetOrCreateAsync(cmd.DriverId, db, ct);
        var req = cmd.Request;

        OnboardingShared.UpsertDocument(db, cmd.DriverId, DocumentType.Insurance, req.FileUrl, null,
            req.ExpiryDate.ToDateTime(TimeOnly.MinValue));

        onb.InsuranceDataJson = System.Text.Json.JsonSerializer.Serialize(req);
        onb.InsuranceStatus   = OnboardingStepStatus.Submitted;

        await db.SaveChangesAsync(ct);
        return await OnboardingShared.BuildStatusAsync(cmd.DriverId, onb, db, ct);
    }
}

// ── POST /driver/onboarding/guarantor ────────────────────────────────────────

public record SubmitGuarantorCommand(string DriverId, SubmitGuarantorRequest Request)
    : IRequest<OnboardingStatusDto>;

public class SubmitGuarantorHandler(IApplicationDbContext db)
    : IRequestHandler<SubmitGuarantorCommand, OnboardingStatusDto>
{
    public async Task<OnboardingStatusDto> Handle(SubmitGuarantorCommand cmd, CancellationToken ct)
    {
        var onb = await OnboardingHelper.GetOrCreateAsync(cmd.DriverId, db, ct);

        onb.GuarantorDataJson = System.Text.Json.JsonSerializer.Serialize(cmd.Request);
        onb.GuarantorStatus   = OnboardingStepStatus.Submitted;

        await db.SaveChangesAsync(ct);
        return await OnboardingShared.BuildStatusAsync(cmd.DriverId, onb, db, ct);
    }
}

// ── POST /driver/onboarding/payout ───────────────────────────────────────────

public record SubmitPayoutCommand(string DriverId, SubmitPayoutRequest Request)
    : IRequest<OnboardingStatusDto>;

public class SubmitPayoutHandler(IApplicationDbContext db)
    : IRequestHandler<SubmitPayoutCommand, OnboardingStatusDto>
{
    public async Task<OnboardingStatusDto> Handle(SubmitPayoutCommand cmd, CancellationToken ct)
    {
        var onb = await OnboardingHelper.GetOrCreateAsync(cmd.DriverId, db, ct);
        var req = cmd.Request;

        // Upsert payout method
        var existingMethod = await db.PayoutMethods
            .FirstOrDefaultAsync(m => m.DriverId == cmd.DriverId && m.IsDefault, ct);

        if (existingMethod == null)
        {
            db.PayoutMethods.Add(new PayoutMethod
            {
                DriverId      = cmd.DriverId,
                Method        = req.Method,
                BankCode      = req.BankCode,
                AccountNumber = req.AccountNumber,
                AccountName   = req.AccountName,
                MobilePhone   = req.MobilePhone,
                IsDefault     = true,
                IsVerified    = false,
            });
        }
        else
        {
            existingMethod.Method        = req.Method;
            existingMethod.BankCode      = req.BankCode;
            existingMethod.AccountNumber = req.AccountNumber;
            existingMethod.AccountName   = req.AccountName;
            existingMethod.MobilePhone   = req.MobilePhone;
        }

        onb.PayoutDataJson = System.Text.Json.JsonSerializer.Serialize(req);
        onb.PayoutStatus   = OnboardingStepStatus.Submitted;

        await db.SaveChangesAsync(ct);
        return await OnboardingShared.BuildStatusAsync(cmd.DriverId, onb, db, ct);
    }
}

// ── POST /driver/onboarding/selfie ────────────────────────────────────────────

public record SubmitSelfieCommand(string DriverId, SubmitSelfieRequest Request)
    : IRequest<OnboardingStatusDto>;

public class SubmitSelfieHandler(IApplicationDbContext db)
    : IRequestHandler<SubmitSelfieCommand, OnboardingStatusDto>
{
    public async Task<OnboardingStatusDto> Handle(SubmitSelfieCommand cmd, CancellationToken ct)
    {
        var onb = await OnboardingHelper.GetOrCreateAsync(cmd.DriverId, db, ct);

        OnboardingShared.UpsertDocument(db, cmd.DriverId, DocumentType.Selfie, cmd.Request.FileUrl, null, null);

        onb.SelfieStatus = OnboardingStepStatus.Submitted;
        await db.SaveChangesAsync(ct);
        return await OnboardingShared.BuildStatusAsync(cmd.DriverId, onb, db, ct);
    }
}

// ── POST /driver/onboarding/submit ────────────────────────────────────────────

public record SubmitOnboardingCommand(string DriverId) : IRequest<OnboardingStatusDto>;

public class SubmitOnboardingHandler(IApplicationDbContext db)
    : IRequestHandler<SubmitOnboardingCommand, OnboardingStatusDto>
{
    public async Task<OnboardingStatusDto> Handle(SubmitOnboardingCommand cmd, CancellationToken ct)
    {
        var onb = await OnboardingHelper.GetOrCreateAsync(cmd.DriverId, db, ct);

        var steps = GetOnboardingHandler.BuildSteps(onb);
        var incomplete = steps.Any(s => s.Status == "empty");
        if (incomplete)
            throw new InvalidOperationException(
                "CONFLICT: All onboarding steps must be submitted before finalising.");

        if (onb.SubmittedAt != null)
            throw new InvalidOperationException(
                "CONFLICT: Application has already been submitted.");

        onb.SubmittedAt = DateTime.UtcNow;

        var dp = await db.DriverProfiles.FirstOrDefaultAsync(d => d.UserId == cmd.DriverId, ct);
        if (dp != null)
            dp.KycStatus = KycStatus.InReview;

        await db.SaveChangesAsync(ct);
        return await OnboardingShared.BuildStatusAsync(cmd.DriverId, onb, db, ct);
    }
}

// ── Shared helpers ────────────────────────────────────────────────────────────

internal static class OnboardingShared
{
    public static void UpsertDocument(
        IApplicationDbContext db, string driverId,
        DocumentType type, string fileUrl, string? backFileUrl, DateTime? expiresAt)
    {
        var existing = db.DriverDocuments
            .Local.FirstOrDefault(d => d.DriverId == driverId && d.Type == type);

        if (existing != null)
        {
            existing.FileUrl          = fileUrl;
            existing.BackFileUrl      = backFileUrl;
            existing.ExpiresAt        = expiresAt;
            existing.Status           = OnboardingStepStatus.Submitted;
            existing.RejectionReason  = null;
        }
        else
        {
            db.DriverDocuments.Add(new DriverDocument
            {
                DriverId     = driverId,
                Type         = type,
                FileUrl      = fileUrl,
                BackFileUrl  = backFileUrl,
                ExpiresAt    = expiresAt,
                Status       = OnboardingStepStatus.Submitted,
            });
        }
    }

    public static async Task<OnboardingStatusDto> BuildStatusAsync(
        string driverId, Domain.Entities.DriverOnboarding onb,
        IApplicationDbContext db, CancellationToken ct)
    {
        var dp    = await db.DriverProfiles.FirstOrDefaultAsync(d => d.UserId == driverId, ct);
        var steps = GetOnboardingHandler.BuildSteps(onb);
        return new OnboardingStatusDto(
            Steps:       steps,
            Progress:    steps.Count(s => s.Status != "empty"),
            CanSubmit:   steps.All(s => s.Status != "empty") && onb.SubmittedAt == null,
            SubmittedAt: onb.SubmittedAt,
            KycStatus:   dp?.KycStatus.ToString().ToLower() ?? "pending");
    }
}
