using Izigo.Application.Common.Interfaces;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Admin.Kyc.Commands;

public record KycCommandResult(bool Success, string? ErrorCode);

// ── Shared step helper ────────────────────────────────────────────────────────

file static class StepHelper
{
    internal static OnboardingStepStatus GetStepStatus(Domain.Entities.DriverOnboarding o, string step) =>
        step.ToLower() switch
        {
            "personal"  => o.PersonalStatus,
            "identity"  => o.IdentityStatus,
            "license"   => o.LicenseStatus,
            "vehicle"   => o.VehicleStatus,
            "insurance" => o.InsuranceStatus,
            "guarantor" => o.GuarantorStatus,
            "payout"    => o.PayoutStatus,
            "selfie"    => o.SelfieStatus,
            _           => throw new ArgumentException($"Unknown step: {step}")
        };

    internal static void SetStepStatus(Domain.Entities.DriverOnboarding o,
        string step, OnboardingStepStatus status, string? reason = null)
    {
        switch (step.ToLower())
        {
            case "personal":   o.PersonalStatus   = status; o.PersonalRejectionReason   = reason; break;
            case "identity":   o.IdentityStatus   = status; o.IdentityRejectionReason   = reason; break;
            case "license":    o.LicenseStatus    = status; o.LicenseRejectionReason    = reason; break;
            case "vehicle":    o.VehicleStatus    = status; o.VehicleRejectionReason    = reason; break;
            case "insurance":  o.InsuranceStatus  = status; o.InsuranceRejectionReason  = reason; break;
            case "guarantor":  o.GuarantorStatus  = status; o.GuarantorRejectionReason  = reason; break;
            case "payout":     o.PayoutStatus     = status; o.PayoutRejectionReason     = reason; break;
            case "selfie":     o.SelfieStatus     = status; break;
        }
    }

    internal static bool AllStepsApproved(Domain.Entities.DriverOnboarding o) =>
        o.PersonalStatus   == OnboardingStepStatus.Approved &&
        o.IdentityStatus   == OnboardingStepStatus.Approved &&
        o.LicenseStatus    == OnboardingStepStatus.Approved &&
        o.VehicleStatus    == OnboardingStepStatus.Approved &&
        o.InsuranceStatus  == OnboardingStepStatus.Approved &&
        o.GuarantorStatus  == OnboardingStepStatus.Approved &&
        o.PayoutStatus     == OnboardingStepStatus.Approved &&
        o.SelfieStatus     == OnboardingStepStatus.Approved;

    internal static string[] UnapprovedSteps(Domain.Entities.DriverOnboarding o)
    {
        var unapproved = new List<string>();
        if (o.PersonalStatus   != OnboardingStepStatus.Approved) unapproved.Add("personal");
        if (o.IdentityStatus   != OnboardingStepStatus.Approved) unapproved.Add("identity");
        if (o.LicenseStatus    != OnboardingStepStatus.Approved) unapproved.Add("license");
        if (o.VehicleStatus    != OnboardingStepStatus.Approved) unapproved.Add("vehicle");
        if (o.InsuranceStatus  != OnboardingStepStatus.Approved) unapproved.Add("insurance");
        if (o.GuarantorStatus  != OnboardingStepStatus.Approved) unapproved.Add("guarantor");
        if (o.PayoutStatus     != OnboardingStepStatus.Approved) unapproved.Add("payout");
        if (o.SelfieStatus     != OnboardingStepStatus.Approved) unapproved.Add("selfie");
        return [.. unapproved];
    }
}

// ── POST /admin/kyc/{driver_id}/steps/{step}/approve ─────────────────────────
// Permission: kyc.approve

public record ApproveKycStepCommand(string DriverId, string Step,
    string StaffId, string StaffName) : IRequest<KycCommandResult>;

public class ApproveKycStepHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<ApproveKycStepCommand, KycCommandResult>
{
    public async Task<KycCommandResult> Handle(ApproveKycStepCommand cmd, CancellationToken ct)
    {
        var onboarding = await db.DriverOnboardings
            .FirstOrDefaultAsync(o => o.DriverId == cmd.DriverId, ct);
        if (onboarding is null) return new(false, "APPLICATION_NOT_FOUND");

        OnboardingStepStatus before;
        try { before = StepHelper.GetStepStatus(onboarding, cmd.Step); }
        catch (ArgumentException) { return new(false, "INVALID_STEP"); }

        StepHelper.SetStepStatus(onboarding, cmd.Step, OnboardingStepStatus.Approved);
        onboarding.ReviewedAt = DateTime.UtcNow;

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.KycStepApprove,
            "DriverOnboarding", cmd.DriverId, reason: $"Step {cmd.Step} approved",
            before: new { Step = cmd.Step, Status = before.ToString() },
            after: new { Step = cmd.Step, Status = "Approved" }, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── POST /admin/kyc/{driver_id}/steps/{step}/reject ──────────────────────────
// reason (required, display-ready) — pushes kyc.status_changed, re-opens just that step

public record RejectKycStepCommand(string DriverId, string Step, string Reason,
    string StaffId, string StaffName) : IRequest<KycCommandResult>;

public class RejectKycStepHandler(IApplicationDbContext db, IAuditService audit,
    IRealtimeService realtime) : IRequestHandler<RejectKycStepCommand, KycCommandResult>
{
    public async Task<KycCommandResult> Handle(RejectKycStepCommand cmd, CancellationToken ct)
    {
        var onboarding = await db.DriverOnboardings
            .FirstOrDefaultAsync(o => o.DriverId == cmd.DriverId, ct);
        if (onboarding is null) return new(false, "APPLICATION_NOT_FOUND");

        OnboardingStepStatus before;
        try { before = StepHelper.GetStepStatus(onboarding, cmd.Step); }
        catch (ArgumentException) { return new(false, "INVALID_STEP"); }

        // reason string is what the driver app displays
        StepHelper.SetStepStatus(onboarding, cmd.Step, OnboardingStepStatus.Rejected, cmd.Reason);

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.KycStepReject,
            "DriverOnboarding", cmd.DriverId, cmd.Reason,
            before: new { Step = cmd.Step, Status = before.ToString() },
            after: new { Step = cmd.Step, Status = "Rejected", Reason = cmd.Reason }, ct: ct);
        await db.SaveChangesAsync(ct);

        // Push kyc.status_changed so the driver app re-opens just that step
        await realtime.PublishToDriverAsync(cmd.DriverId, "kyc.status_changed",
            new { step = cmd.Step, status = "rejected", reason = cmd.Reason }, ct);

        return new(true, null);
    }
}

// ── POST /admin/kyc/{driver_id}/approve ──────────────────────────────────────
// 422 if any step not approved. Sets kyc_status=approved and lets driver go online.

public record ApproveKycApplicationCommand(string DriverId, string StaffId, string StaffName)
    : IRequest<KycCommandResult>;

public class ApproveKycApplicationHandler(IApplicationDbContext db, IAuditService audit,
    IRealtimeService realtime) : IRequestHandler<ApproveKycApplicationCommand, KycCommandResult>
{
    public async Task<KycCommandResult> Handle(ApproveKycApplicationCommand cmd, CancellationToken ct)
    {
        var onboarding = await db.DriverOnboardings
            .FirstOrDefaultAsync(o => o.DriverId == cmd.DriverId, ct);
        if (onboarding is null) return new(false, "APPLICATION_NOT_FOUND");

        // 422 if any step is not approved
        if (!StepHelper.AllStepsApproved(onboarding))
        {
            var unapproved = StepHelper.UnapprovedSteps(onboarding);
            return new(false, $"STEPS_NOT_APPROVED:{string.Join(",", unapproved)}");
        }

        var driver = await db.DriverProfiles.FirstOrDefaultAsync(d => d.Id == cmd.DriverId, ct);
        if (driver is null) return new(false, "DRIVER_NOT_FOUND");

        var before = new { driver.KycStatus, driver.OnboardingComplete };
        driver.KycStatus          = KycStatus.Approved;
        driver.OnboardingComplete = true;
        onboarding.ReviewedAt     = DateTime.UtcNow;

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.KycApprove,
            "DriverOnboarding", cmd.DriverId, reason: "Full application approved",
            before: before, after: new { KycStatus = "Approved", OnboardingComplete = true }, ct: ct);
        await db.SaveChangesAsync(ct);

        // Notify driver they can go online
        await realtime.PublishToDriverAsync(cmd.DriverId, "kyc.status_changed",
            new { status = "approved" }, ct);

        return new(true, null);
    }
}

// ── POST /admin/kyc/{driver_id}/reject ───────────────────────────────────────

public record RejectKycApplicationCommand(string DriverId, string Reason, bool Blocklist,
    string StaffId, string StaffName) : IRequest<KycCommandResult>;

public class RejectKycApplicationHandler(IApplicationDbContext db, IAuditService audit,
    IRealtimeService realtime) : IRequestHandler<RejectKycApplicationCommand, KycCommandResult>
{
    public async Task<KycCommandResult> Handle(RejectKycApplicationCommand cmd, CancellationToken ct)
    {
        var driver = await db.DriverProfiles
            .Include(d => d.User)
            .FirstOrDefaultAsync(d => d.Id == cmd.DriverId, ct);
        if (driver is null) return new(false, "DRIVER_NOT_FOUND");

        var before = new { driver.KycStatus };
        driver.KycStatus = KycStatus.Rejected;

        if (cmd.Blocklist)
            driver.User.Status = UserStatus.Blocked; // fraud blocklist

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.KycReject,
            "DriverOnboarding", cmd.DriverId, cmd.Reason,
            before, new { KycStatus = "Rejected", Blocklisted = cmd.Blocklist }, ct: ct);
        await db.SaveChangesAsync(ct);

        await realtime.PublishToDriverAsync(cmd.DriverId, "kyc.status_changed",
            new { status = "rejected", reason = cmd.Reason }, ct);

        return new(true, null);
    }
}

// ── POST /admin/kyc/{driver_id}/assign ───────────────────────────────────────
// Claim for review — stops two reviewers working the same file.

public record AssignKycReviewerCommand(string DriverId, string StaffId, string StaffName)
    : IRequest<KycCommandResult>;

public class AssignKycReviewerHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<AssignKycReviewerCommand, KycCommandResult>
{
    public async Task<KycCommandResult> Handle(AssignKycReviewerCommand cmd, CancellationToken ct)
    {
        var onboarding = await db.DriverOnboardings
            .FirstOrDefaultAsync(o => o.DriverId == cmd.DriverId, ct);
        if (onboarding is null) return new(false, "APPLICATION_NOT_FOUND");

        // If already assigned to another reviewer, reject
        if (onboarding.AssignedReviewerStaffId is not null &&
            onboarding.AssignedReviewerStaffId != cmd.StaffId)
            return new(false, "ALREADY_ASSIGNED");

        onboarding.AssignedReviewerStaffId = cmd.StaffId;

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.KycStepApprove,
            "DriverOnboarding", cmd.DriverId, reason: "KYC application claimed for review",
            after: new { AssignedTo = cmd.StaffId }, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── POST /admin/documents/{id}/review ────────────────────────────────────────
// Approve a renewal document: decision, reason?, expires_at

public record ReviewDocumentCommand(string DocId, string Decision, string? Reason,
    DateTime? ExpiresAt, string StaffId, string StaffName) : IRequest<KycCommandResult>;

public class ReviewDocumentHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<ReviewDocumentCommand, KycCommandResult>
{
    public async Task<KycCommandResult> Handle(ReviewDocumentCommand cmd, CancellationToken ct)
    {
        var doc = await db.DriverDocuments.FirstOrDefaultAsync(d => d.Id == cmd.DocId, ct);
        if (doc is null) return new(false, "DOCUMENT_NOT_FOUND");

        var before = new { doc.Status, doc.ExpiresAt };

        doc.Status           = cmd.Decision.ToLower() == "approve"
            ? OnboardingStepStatus.Approved : OnboardingStepStatus.Rejected;
        doc.RejectionReason  = cmd.Decision.ToLower() == "reject" ? cmd.Reason : null;
        doc.ReviewedByStaffId = cmd.StaffId;
        doc.ReviewedAt       = DateTime.UtcNow;

        if (cmd.Decision.ToLower() == "approve" && cmd.ExpiresAt.HasValue)
            doc.ExpiresAt = cmd.ExpiresAt;

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.DocumentAccess,
            "DriverDocument", cmd.DocId, cmd.Reason,
            before, new { Status = doc.Status.ToString(), doc.ExpiresAt }, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}
