using Izigo.Domain.Common;
using Izigo.Domain.Enums;

namespace Izigo.Domain.Entities;

public class Vehicle : BaseEntity
{
    public Vehicle() => Id = EntityId.ForVehicle();

    public string DriverId { get; set; } = string.Empty;
    public VehicleType Type { get; set; }
    public string Make { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Year { get; set; }
    public string Color { get; set; } = string.Empty;
    public string Plate { get; set; } = string.Empty;
    public int Seats { get; set; }
    public bool IsActive { get; set; }
    public bool PendingReview { get; set; }
}

public class DriverDocument : BaseEntity
{
    public DriverDocument() => Id = EntityId.ForDriverDocument();

    public string DriverId { get; set; } = string.Empty;
    public DocumentType Type { get; set; }
    public string FileUrl { get; set; } = string.Empty;
    public string? BackFileUrl { get; set; }
    public OnboardingStepStatus Status { get; set; } = OnboardingStepStatus.Submitted;
    public string? RejectionReason { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? ReviewedByStaffId { get; set; }
    public DateTime? ReviewedAt { get; set; }
}

public class DriverOnboarding : BaseEntity
{
    public DriverOnboarding() => Id = EntityId.ForDriverOnboarding();

    public string DriverId { get; set; } = string.Empty;
    public DriverProfile Driver { get; set; } = null!;

    public OnboardingStepStatus PersonalStatus { get; set; }
    public string? PersonalRejectionReason { get; set; }
    public OnboardingStepStatus IdentityStatus { get; set; }
    public string? IdentityRejectionReason { get; set; }
    public OnboardingStepStatus LicenseStatus { get; set; }
    public string? LicenseRejectionReason { get; set; }
    public OnboardingStepStatus VehicleStatus { get; set; }
    public string? VehicleRejectionReason { get; set; }
    public OnboardingStepStatus InsuranceStatus { get; set; }
    public string? InsuranceRejectionReason { get; set; }
    public OnboardingStepStatus GuarantorStatus { get; set; }
    public string? GuarantorRejectionReason { get; set; }
    public OnboardingStepStatus PayoutStatus { get; set; }
    public string? PayoutRejectionReason { get; set; }
    public OnboardingStepStatus SelfieStatus { get; set; }
    public string? SelfieRejectionReason { get; set; }

    public string? PersonalDataJson { get; set; }
    public string? IdentityDataJson { get; set; }
    public string? LicenseDataJson { get; set; }
    public string? VehicleDataJson { get; set; }
    public string? InsuranceDataJson { get; set; }
    public string? GuarantorDataJson { get; set; }
    public string? PayoutDataJson { get; set; }

    public DateTime? SubmittedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? AssignedReviewerStaffId { get; set; }
    public decimal? FaceMatchScore { get; set; }
}

public class DriverLocationPoint : BaseEntity
{
    public string DriverId { get; set; } = string.Empty;
    public decimal Lat { get; set; }
    public decimal Lng { get; set; }
    public decimal? Heading { get; set; }
    public decimal? Speed { get; set; }
    public decimal? Accuracy { get; set; }
    public DateTime RecordedAt { get; set; }
    public string? JobId { get; set; }
}
