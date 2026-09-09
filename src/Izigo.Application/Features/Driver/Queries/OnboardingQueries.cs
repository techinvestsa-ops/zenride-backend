using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Driver.Dtos;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Driver.Queries;

// ── GET /driver/onboarding ────────────────────────────────────────────────────

public record GetOnboardingQuery(string DriverId) : IRequest<OnboardingStatusDto>;

public class GetOnboardingHandler(IApplicationDbContext db)
    : IRequestHandler<GetOnboardingQuery, OnboardingStatusDto>
{
    public async Task<OnboardingStatusDto> Handle(GetOnboardingQuery req, CancellationToken ct)
    {
        var onb = await OnboardingHelper.GetOrCreateAsync(req.DriverId, db, ct);
        var dp  = await db.DriverProfiles.FirstOrDefaultAsync(d => d.UserId == req.DriverId, ct);

        var steps = BuildSteps(onb);
        var canSubmit = steps.All(s => s.Status != "empty") && onb.SubmittedAt == null;
        var progress  = steps.Count(s => s.Status != "empty");

        return new OnboardingStatusDto(
            Steps:       steps,
            Progress:    progress,
            CanSubmit:   canSubmit,
            SubmittedAt: onb.SubmittedAt,
            KycStatus:   dp?.KycStatus.ToString().ToLower() ?? "pending");
    }

    internal static OnboardingStepDto[] BuildSteps(Domain.Entities.DriverOnboarding onb) =>
    [
        Step("personal",  "Personal Information", onb.PersonalStatus,  onb.PersonalRejectionReason,
             ["first_name","last_name","date_of_birth","gender","address","city"], []),
        Step("identity",  "Identity Document",    onb.IdentityStatus,  onb.IdentityRejectionReason,
             ["document_number"], ["government_id"]),
        Step("license",   "Driving Licence",      onb.LicenseStatus,   onb.LicenseRejectionReason,
             ["license_number","issue_date","expiry_date"], ["drivers_license"]),
        Step("vehicle",   "Vehicle Details",      onb.VehicleStatus,   onb.VehicleRejectionReason,
             ["make","model","year","color","plate","vehicle_type","seats"], ["vehicle_registration"]),
        Step("insurance", "Insurance",            onb.InsuranceStatus, onb.InsuranceRejectionReason,
             ["policy_number","expiry_date"], ["insurance"]),
        Step("guarantor", "Guarantor",            onb.GuarantorStatus, onb.GuarantorRejectionReason,
             ["name","phone","relationship","occupation","address"], []),
        Step("payout",    "Payout Account",       onb.PayoutStatus,    onb.PayoutRejectionReason,
             ["method","account_number","account_name"], []),
        Step("selfie",    "Live Selfie",          onb.SelfieStatus,    onb.SelfieRejectionReason,
             [], ["selfie"]),
    ];

    private static OnboardingStepDto Step(string key, string label,
        OnboardingStepStatus status, string? rejection,
        string[] requiredFields, string[] requiredDocs) =>
        new(key, label, status.ToString().ToLower(), rejection, requiredFields, requiredDocs);
}

// ── GET /driver/kyc ───────────────────────────────────────────────────────────

public record GetKycQuery(string DriverId) : IRequest<KycDetailDto>;

public class GetKycHandler(IApplicationDbContext db)
    : IRequestHandler<GetKycQuery, KycDetailDto>
{
    public async Task<KycDetailDto> Handle(GetKycQuery req, CancellationToken ct)
    {
        var onb = await OnboardingHelper.GetOrCreateAsync(req.DriverId, db, ct);
        var dp  = await db.DriverProfiles.FirstOrDefaultAsync(d => d.UserId == req.DriverId, ct);

        var steps        = GetOnboardingHandler.BuildSteps(onb);
        var rejectedKeys = steps.Where(s => s.Status == "rejected").Select(s => s.Key).ToArray();

        return new KycDetailDto(
            KycStatus:      dp?.KycStatus.ToString().ToLower() ?? "pending",
            Steps:          steps,
            RejectedSteps:  rejectedKeys,
            SubmittedAt:    onb.SubmittedAt,
            ReviewedAt:     onb.ReviewedAt,
            EstimatedHours: dp?.KycStatus == Domain.Enums.KycStatus.InReview ? 24 : null,
            FaceMatchScore: onb.FaceMatchScore);
    }
}

// ── GET /driver/banks ─────────────────────────────────────────────────────────

public record GetBanksQuery(string Market) : IRequest<List<BankDto>>;

public class GetBanksHandler : IRequestHandler<GetBanksQuery, List<BankDto>>
{
    // Static list for CI market — pulled from real bank API in production
    private static readonly List<BankDto> CiBanks =
    [
        new("SGCI",   "Société Générale Côte d'Ivoire", "SG"),
        new("BICICI", "Banque Internationale pour le Commerce et l'Industrie de la Côte d'Ivoire", "BICICI"),
        new("ECOBANK","Ecobank Côte d'Ivoire",          "Ecobank"),
        new("BOA",    "Bank of Africa Côte d'Ivoire",   "BOA"),
        new("NSIA",   "NSIA Banque",                    "NSIA"),
        new("CORIS",  "Coris Bank International",       "Coris"),
        new("BNI",    "Banque Nationale d'Investissement","BNI"),
        new("WAVE",   "Wave Mobile Money",              "Wave"),
        new("OM",     "Orange Money",                   "OM"),
        new("MOOV",   "Moov Money",                     "Moov"),
    ];

    public Task<List<BankDto>> Handle(GetBanksQuery req, CancellationToken ct)
        => Task.FromResult(CiBanks);
}

// ── POST /driver/payout/resolve-account ──────────────────────────────────────

public record ResolveAccountQuery(ResolveAccountRequest Request) : IRequest<ResolveAccountDto>;

public class ResolveAccountHandler : IRequestHandler<ResolveAccountQuery, ResolveAccountDto>
{
    public Task<ResolveAccountDto> Handle(ResolveAccountQuery req, CancellationToken ct)
    {
        // TODO: call real bank verification API (e.g. Paystack /bank/resolve or CinetPay equivalent)
        // For now return a plausible stub so the onboarding UI can progress
        return Task.FromResult(new ResolveAccountDto(
            AccountNumber: req.Request.AccountNumber,
            AccountName:   "ACCOUNT HOLDER",   // real API returns actual name
            BankCode:      req.Request.BankCode,
            BankName:      "Verified Bank"));
    }
}

// ── Internal helper ───────────────────────────────────────────────────────────

internal static class OnboardingHelper
{
    public static async Task<Domain.Entities.DriverOnboarding> GetOrCreateAsync(
        string driverId, IApplicationDbContext db, CancellationToken ct)
    {
        var onb = await db.DriverOnboardings
            .FirstOrDefaultAsync(o => o.DriverId == driverId, ct);

        if (onb != null) return onb;

        onb = new Domain.Entities.DriverOnboarding { DriverId = driverId };
        db.DriverOnboardings.Add(onb);
        await db.SaveChangesAsync(ct);
        return onb;
    }
}
