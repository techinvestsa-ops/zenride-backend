namespace Izigo.Application.Features.Driver.Dtos;

// ── GET /driver/onboarding ────────────────────────────────────────────────────

public record OnboardingStatusDto(
    OnboardingStepDto[] Steps,
    int Progress,               // spec: progress (e.g. 3 of 8 complete)
    bool CanSubmit,
    DateTime? SubmittedAt,
    string KycStatus
);

public record OnboardingStepDto(
    string Key,
    string Label,
    string Status,              // empty | submitted | approved | rejected
    string? RejectionReason,
    string[] RequiredFields,    // spec: required_fields[]
    string[] RequiredDocuments  // spec: required_documents[]
);

// ── GET /driver/kyc ───────────────────────────────────────────────────────────

public record KycDetailDto(
    string KycStatus,
    OnboardingStepDto[] Steps,
    string[] RejectedSteps,     // spec: rejected_steps[]
    DateTime? SubmittedAt,
    DateTime? ReviewedAt,
    int? EstimatedHours,        // spec: estimated_hours
    decimal? FaceMatchScore
);

// ── GET /driver/banks ─────────────────────────────────────────────────────────

public record BankDto(string Code, string Name, string? ShortName);

// ── POST /driver/payout/resolve-account ──────────────────────────────────────

public record ResolveAccountDto(
    string AccountNumber,
    string AccountName,
    string BankCode,
    string BankName
);

// ── Onboarding step request bodies ───────────────────────────────────────────

public record SubmitPersonalRequest(
    string FirstName,
    string LastName,
    DateOnly DateOfBirth,
    string Gender,
    string Address,
    string City,
    string? NationalIdNumber,
    string? EmergencyContactName,   // spec: emergency contact in personal step
    string? EmergencyContactPhone
);

public record SubmitIdentityRequest(
    string FileUrl,
    string? BackFileUrl,
    string DocumentNumber,
    DateOnly? ExpiryDate
);

public record SubmitLicenseRequest(
    string FileUrl,
    string LicenseNumber,
    DateOnly IssueDate,
    DateOnly ExpiryDate,
    string[] Categories
);

public record SubmitVehicleRequest(
    string Make,
    string Model,
    int Year,
    string Color,
    string Plate,
    string VehicleType,
    int Seats
);

public record SubmitInsuranceRequest(
    string FileUrl,
    string PolicyNumber,
    DateOnly ExpiryDate
);

public record SubmitGuarantorRequest(
    string Name,
    string Phone,
    string Relationship,
    string Occupation,       // spec includes occupation
    string Address,
    string? IdFileUrl
);

public record SubmitPayoutRequest(
    string Method,
    string? BankCode,
    string? AccountNumber,
    string? AccountName,
    string? MobilePhone
);

public record SubmitSelfieRequest(string FileUrl);

public record ResolveAccountRequest(string BankCode, string AccountNumber);
