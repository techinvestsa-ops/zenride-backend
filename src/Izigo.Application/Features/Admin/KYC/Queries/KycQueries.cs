using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Models;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Admin.Kyc.Queries;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record KycDocumentDto(string Id, string Type, string Status, string? SignedUrl,
    DateTime? ExpiresAt, string? RejectionReason);

public record KycStepDto(string Key, string Status, string? ValuesJson,
    string? RejectionReason, IEnumerable<KycDocumentDto> Documents);

public record KycQueueItemDto(
    string DriverId, string DriverName, string PhoneMasked,
    DateTime? SubmittedAt, double HoursWaiting, bool PastSla,
    string? AssignedReviewerStaffId, IEnumerable<KycStepDto> Steps);

public record KycAutomatedChecksDto(string? IdBureauResult, decimal? FaceMatchScore,
    string? BankNameEnquiry, bool DuplicateDeviceHit, bool PlateAlreadyRegistered);

public record KycDetailDto(KycQueueItemDto Application, KycAutomatedChecksDto AutomatedChecks);

public record SignedDocumentUrlDto(string DriverId, string DocId, string Url, DateTime ExpiresAt);

public record KycReviewerStatDto(string StaffId, int ApprovalsToday);
public record KycRejectionReasonStatDto(string Reason, int Count);
public record KycMetricsDto(int QueueDepth, double? OldestWaitHours,
    IEnumerable<KycReviewerStatDto> ApprovalsPerReviewer,
    IEnumerable<KycRejectionReasonStatDto> RejectionReasonsRanked);

public record ExpiringDocumentDto(string DriverId, string DriverName,
    string DocumentType, string? DocumentStatus, DateTime ExpiresAt, int DaysUntilExpiry);

// ── Internal projection types (not exposed in API) ───────────────────────────

internal sealed record KycDocData(string Id, string DriverId, DocumentType Type,
    OnboardingStepStatus Status, string FileUrl, DateTime? ExpiresAt, string? RejectionReason);

internal sealed record KycOnboardingData(
    string DriverId, DateTime? SubmittedAt, string? AssignedReviewerStaffId,
    OnboardingStepStatus PersonalStatus,   string? PersonalDataJson,   string? PersonalRejectionReason,
    OnboardingStepStatus IdentityStatus,   string? IdentityDataJson,   string? IdentityRejectionReason,
    OnboardingStepStatus LicenseStatus,    string? LicenseDataJson,    string? LicenseRejectionReason,
    OnboardingStepStatus VehicleStatus,    string? VehicleDataJson,    string? VehicleRejectionReason,
    OnboardingStepStatus InsuranceStatus,  string? InsuranceDataJson,  string? InsuranceRejectionReason,
    OnboardingStepStatus GuarantorStatus,  string? GuarantorDataJson,  string? GuarantorRejectionReason,
    OnboardingStepStatus PayoutStatus,     string? PayoutDataJson,     string? PayoutRejectionReason,
    OnboardingStepStatus SelfieStatus,     decimal? FaceMatchScore);

// ── Shared step builder ───────────────────────────────────────────────────────

internal static class KycStepBuilder
{
    private static DocumentType[] StepDocTypes(string step) => step switch
    {
        "identity"  => [DocumentType.GovernmentId],
        "license"   => [DocumentType.DriversLicense],
        "vehicle"   => [DocumentType.VehicleRegistration, DocumentType.VehiclePhoto],
        "insurance" => [DocumentType.Insurance],
        "guarantor" => [DocumentType.GuarantorId],
        "selfie"    => [DocumentType.Selfie],
        _           => []
    };

    private static KycDocumentDto MapDoc(KycDocData d, IFileStorageService storage)
    {
        var signedUrl = string.IsNullOrEmpty(d.FileUrl)
            ? null : storage.GetSignedUrl(d.FileUrl, 15);
        return new KycDocumentDto(d.Id, d.Type.ToString(), d.Status.ToString(),
            signedUrl, d.ExpiresAt, d.RejectionReason);
    }

    public static IEnumerable<KycStepDto> Build(KycOnboardingData o,
        IReadOnlyList<KycDocData> docs, IFileStorageService storage)
    {
        List<KycDocumentDto> GetDocs(string step)
        {
            var types = StepDocTypes(step);
            return docs.Where(d => types.Contains(d.Type))
                       .Select(d => MapDoc(d, storage)).ToList();
        }

        return
        [
            new("personal",   o.PersonalStatus.ToString(),   o.PersonalDataJson,   o.PersonalRejectionReason,   GetDocs("personal")),
            new("identity",   o.IdentityStatus.ToString(),   o.IdentityDataJson,   o.IdentityRejectionReason,   GetDocs("identity")),
            new("license",    o.LicenseStatus.ToString(),    o.LicenseDataJson,    o.LicenseRejectionReason,    GetDocs("license")),
            new("vehicle",    o.VehicleStatus.ToString(),    o.VehicleDataJson,    o.VehicleRejectionReason,    GetDocs("vehicle")),
            new("insurance",  o.InsuranceStatus.ToString(),  o.InsuranceDataJson,  o.InsuranceRejectionReason,  GetDocs("insurance")),
            new("guarantor",  o.GuarantorStatus.ToString(),  o.GuarantorDataJson,  o.GuarantorRejectionReason,  GetDocs("guarantor")),
            new("payout",     o.PayoutStatus.ToString(),     o.PayoutDataJson,     o.PayoutRejectionReason,     GetDocs("payout")),
            new("selfie",     o.SelfieStatus.ToString(),     null,                 null,                        GetDocs("selfie")),
        ];
    }
}

// ── GET /admin/kyc/queue ──────────────────────────────────────────────────────

public record GetKycQueueQuery(string? Q, int Page, int PerPage) : IRequest<object>;

public class GetKycQueueHandler(IApplicationDbContext db, IFileStorageService storage)
    : IRequestHandler<GetKycQueueQuery, object>
{
    private const double SlaHours = 48;

    public async Task<object> Handle(GetKycQueueQuery req, CancellationToken ct)
    {
        var baseQuery = db.DriverOnboardings
            .Where(o => o.SubmittedAt != null && o.ReviewedAt == null);

        var perPage  = Math.Max(1, Math.Min(100, req.PerPage));
        var total    = await baseQuery.CountAsync(ct);
        var lastPage = (int)Math.Ceiling((double)total / perPage);

        var onboardings = await baseQuery
            .OrderBy(o => o.SubmittedAt)
            .Skip((req.Page - 1) * perPage)
            .Take(perPage)
            .Select(o => new KycOnboardingData(
                o.DriverId, o.SubmittedAt, o.AssignedReviewerStaffId,
                o.PersonalStatus,   o.PersonalDataJson,   o.PersonalRejectionReason,
                o.IdentityStatus,   o.IdentityDataJson,   o.IdentityRejectionReason,
                o.LicenseStatus,    o.LicenseDataJson,    o.LicenseRejectionReason,
                o.VehicleStatus,    o.VehicleDataJson,    o.VehicleRejectionReason,
                o.InsuranceStatus,  o.InsuranceDataJson,  o.InsuranceRejectionReason,
                o.GuarantorStatus,  o.GuarantorDataJson,  o.GuarantorRejectionReason,
                o.PayoutStatus,     o.PayoutDataJson,     o.PayoutRejectionReason,
                o.SelfieStatus,     o.FaceMatchScore))
            .ToListAsync(ct);

        var driverIds = onboardings.Select(o => o.DriverId).ToList();

        var drivers = await db.DriverProfiles
            .Where(d => driverIds.Contains(d.Id))
            .Select(d => new { d.Id, d.User.FirstName, d.User.LastName, d.User.Phone })
            .ToListAsync(ct);
        var driverMap = drivers.ToDictionary(d => d.Id);

        var docs = await db.DriverDocuments
            .Where(d => driverIds.Contains(d.DriverId))
            .Select(d => new KycDocData(d.Id, d.DriverId, d.Type, d.Status, d.FileUrl, d.ExpiresAt, d.RejectionReason))
            .ToListAsync(ct);
        var docsByDriver = docs.GroupBy(d => d.DriverId).ToDictionary(g => g.Key, g => (IReadOnlyList<KycDocData>)g.ToList());

        var now   = DateTime.UtcNow;
        var items = onboardings.Select(o =>
        {
            driverMap.TryGetValue(o.DriverId, out var drv);
            var driverDocs = docsByDriver.GetValueOrDefault(o.DriverId, []);
            var hoursWaiting = o.SubmittedAt.HasValue
                ? (now - o.SubmittedAt.Value).TotalHours : 0;

            return new KycQueueItemDto(
                o.DriverId,
                drv is not null ? $"{drv.FirstName} {drv.LastName}".Trim() : "—",
                drv is not null ? Ops.Queries.GetLiveOpsHandler.MaskPhone(drv.Phone) : "—",
                o.SubmittedAt, hoursWaiting, hoursWaiting > SlaHours,
                o.AssignedReviewerStaffId,
                KycStepBuilder.Build(o, driverDocs, storage));
        }).ToArray();

        return AdminApiResponse.Ok(items, new AdminPagedMeta(req.Page, perPage, total, lastPage));
    }
}

// ── GET /admin/kyc/{driver_id} ────────────────────────────────────────────────

public record GetKycDetailQuery(string DriverId) : IRequest<KycDetailDto?>;

public class GetKycDetailHandler(IApplicationDbContext db, IFileStorageService storage)
    : IRequestHandler<GetKycDetailQuery, KycDetailDto?>
{
    public async Task<KycDetailDto?> Handle(GetKycDetailQuery req, CancellationToken ct)
    {
        var onboarding = await db.DriverOnboardings
            .Where(o => o.DriverId == req.DriverId)
            .Select(o => new KycOnboardingData(
                o.DriverId, o.SubmittedAt, o.AssignedReviewerStaffId,
                o.PersonalStatus,   o.PersonalDataJson,   o.PersonalRejectionReason,
                o.IdentityStatus,   o.IdentityDataJson,   o.IdentityRejectionReason,
                o.LicenseStatus,    o.LicenseDataJson,    o.LicenseRejectionReason,
                o.VehicleStatus,    o.VehicleDataJson,    o.VehicleRejectionReason,
                o.InsuranceStatus,  o.InsuranceDataJson,  o.InsuranceRejectionReason,
                o.GuarantorStatus,  o.GuarantorDataJson,  o.GuarantorRejectionReason,
                o.PayoutStatus,     o.PayoutDataJson,     o.PayoutRejectionReason,
                o.SelfieStatus,     o.FaceMatchScore))
            .FirstOrDefaultAsync(ct);

        if (onboarding is null) return null;

        var driver = await db.DriverProfiles
            .Where(d => d.Id == req.DriverId)
            .Select(d => new { d.Id, d.UserId, d.User.FirstName, d.User.LastName, d.User.Phone })
            .FirstOrDefaultAsync(ct);

        var docs = await db.DriverDocuments
            .Where(d => d.DriverId == req.DriverId)
            .Select(d => new KycDocData(d.Id, d.DriverId, d.Type, d.Status, d.FileUrl, d.ExpiresAt, d.RejectionReason))
            .ToListAsync(ct);

        var steps = KycStepBuilder.Build(onboarding, docs, storage);

        var now          = DateTime.UtcNow;
        var hoursWaiting = onboarding.SubmittedAt.HasValue
            ? (now - onboarding.SubmittedAt.Value).TotalHours : 0;

        // Automated checks
        var vehiclePlate = await db.Vehicles
            .Where(v => v.DriverId == req.DriverId && v.IsActive)
            .Select(v => v.Plate).FirstOrDefaultAsync(ct);

        var plateExists = vehiclePlate is not null && await db.Vehicles
            .AnyAsync(v => v.Plate == vehiclePlate && v.DriverId != req.DriverId, ct);

        var duplicateDevice = driver is not null && await db.UserDevices
            .Where(d => d.UserId == driver.UserId)
            .AnyAsync(d => db.UserDevices.Any(o => o.DeviceId == d.DeviceId && o.UserId != driver.UserId), ct);

        var app = new KycQueueItemDto(
            req.DriverId,
            driver is not null ? $"{driver.FirstName} {driver.LastName}".Trim() : "—",
            driver is not null ? Ops.Queries.GetLiveOpsHandler.MaskPhone(driver.Phone) : "—",
            onboarding.SubmittedAt, hoursWaiting, hoursWaiting > 48,
            onboarding.AssignedReviewerStaffId, steps);

        var checks = new KycAutomatedChecksDto(
            IdBureauResult:         null,
            FaceMatchScore:         onboarding.FaceMatchScore,
            BankNameEnquiry:        null,
            DuplicateDeviceHit:     duplicateDevice,
            PlateAlreadyRegistered: plateExists);

        return new KycDetailDto(app, checks);
    }
}

// ── GET /admin/kyc/{driver_id}/documents/{doc_id} ─────────────────────────────

public record GetKycDocumentUrlQuery(string DriverId, string DocId,
    string StaffId, string StaffName) : IRequest<SignedDocumentUrlDto?>;

public class GetKycDocumentUrlHandler(IApplicationDbContext db, IFileStorageService storage,
    IAuditService audit) : IRequestHandler<GetKycDocumentUrlQuery, SignedDocumentUrlDto?>
{
    public async Task<SignedDocumentUrlDto?> Handle(GetKycDocumentUrlQuery req, CancellationToken ct)
    {
        var doc = await db.DriverDocuments
            .Where(d => d.Id == req.DocId && d.DriverId == req.DriverId)
            .Select(d => new { d.Id, d.FileUrl })
            .FirstOrDefaultAsync(ct);

        if (doc is null || string.IsNullOrEmpty(doc.FileUrl)) return null;

        var signedUrl = storage.GetSignedUrl(doc.FileUrl, 15);
        var expiresAt = DateTime.UtcNow.AddMinutes(15);

        await audit.RecordAsync(req.StaffId, req.StaffName, AuditAction.DocumentAccess,
            "DriverDocument", req.DocId,
            reason: $"KYC document viewed for driver {req.DriverId}", ct: ct);

        return new SignedDocumentUrlDto(req.DriverId, req.DocId, signedUrl, expiresAt);
    }
}

// ── GET /admin/kyc/metrics ────────────────────────────────────────────────────

public record GetKycMetricsQuery : IRequest<KycMetricsDto>;

public class GetKycMetricsHandler(IApplicationDbContext db)
    : IRequestHandler<GetKycMetricsQuery, KycMetricsDto>
{
    public async Task<KycMetricsDto> Handle(GetKycMetricsQuery req, CancellationToken ct)
    {
        var pending = await db.DriverOnboardings
            .Where(o => o.SubmittedAt != null && o.ReviewedAt == null)
            .Select(o => o.SubmittedAt)
            .ToListAsync(ct);

        var now          = DateTime.UtcNow;
        var queueDepth   = pending.Count;
        var oldestWait   = pending.Any()
            ? (now - pending.Min()!.Value).TotalHours : (double?)null;

        var yesterday = now.AddHours(-24);
        var approvals = await db.AuditLogs
            .Where(a => a.Action == AuditAction.KycApprove && a.CreatedAt >= yesterday)
            .GroupBy(a => a.ActorId)
            .Select(g => new KycReviewerStatDto(g.Key, g.Count()))
            .ToListAsync(ct);

        var rejections = await db.AuditLogs
            .Where(a => a.Action == AuditAction.KycStepReject && a.Reason != null)
            .GroupBy(a => a.Reason!)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => new KycRejectionReasonStatDto(g.Key, g.Count()))
            .ToListAsync(ct);

        return new KycMetricsDto(queueDepth, oldestWait, approvals, rejections);
    }
}

// ── GET /admin/documents/expiring ────────────────────────────────────────────

public record GetExpiringDocumentsQuery(int WithinDays) : IRequest<object>;

public class GetExpiringDocumentsHandler(IApplicationDbContext db)
    : IRequestHandler<GetExpiringDocumentsQuery, object>
{
    public async Task<object> Handle(GetExpiringDocumentsQuery req, CancellationToken ct)
    {
        var now    = DateTime.UtcNow;
        var cutoff = now.AddDays(req.WithinDays);

        var docs = await db.DriverDocuments
            .Where(d => d.Status == OnboardingStepStatus.Approved
                     && d.ExpiresAt.HasValue
                     && d.ExpiresAt.Value >= now
                     && d.ExpiresAt.Value <= cutoff)
            .Select(d => new { d.Id, d.DriverId, d.Type, d.Status, d.ExpiresAt })
            .ToListAsync(ct);

        var driverIds = docs.Select(d => d.DriverId).Distinct().ToList();
        var drivers   = await db.DriverProfiles
            .Where(d => driverIds.Contains(d.Id))
            .Select(d => new { d.Id, d.User.FirstName, d.User.LastName })
            .ToListAsync(ct);
        var driverMap = drivers.ToDictionary(d => d.Id);

        var result = docs.Select(d =>
        {
            driverMap.TryGetValue(d.DriverId, out var drv);
            return new ExpiringDocumentDto(
                d.DriverId,
                drv is not null ? $"{drv.FirstName} {drv.LastName}".Trim() : "—",
                d.Type.ToString(),
                d.Status.ToString(),
                d.ExpiresAt!.Value,
                (int)Math.Ceiling((d.ExpiresAt.Value - now).TotalDays));
        }).OrderBy(d => d.ExpiresAt).ToArray();

        return AdminApiResponse.Ok(result, new AdminPagedMeta(1, result.Length, result.Length, 1));
    }
}
