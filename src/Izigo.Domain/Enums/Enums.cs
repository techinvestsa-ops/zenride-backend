namespace Izigo.Domain.Enums;

public enum UserRole { Rider, Driver }

public enum UserStatus { Active, Suspended, Blocked }

public enum KycStatus { Pending, InReview, Approved, Rejected }

public enum Vertical { Ride, CoRide, Package }

public enum ServiceClass { ZenCar, ZenBike, ZenCoRide, PackageSmall, PackageLarge }

// Rider-facing ride status (projection of driver job state)
public enum RideStatus
{
    Searching,
    DriverAssigned,
    DriverArriving,
    DriverArrived,
    InProgress,
    Completed,
    Cancelled,
    NoDriversFound
}

// Driver canonical job state machine
public enum JobState
{
    Broadcasting,
    Offered,
    Accepted,
    EnRouteToPickup,
    ArrivedAtPickup,
    PickedUp,
    EnRouteToDropoff,
    ArrivedAtDropoff,
    Completed,
    CancelledByRider,
    CancelledByDriver,
    CancelledByAdmin,
    Expired,
    RejectedByDriver,
    Returned,
    Disputed
}

public enum PackageStatus { Searching, Matched, PickedUp, InTransit, Delivered, Cancelled, Returned }

public enum CoRideBookingStatus { Upcoming, DriverArriving, InRide, Completed, Cancelled }

public enum PaymentMethod { Cash, Wallet, Card, OrangeMoney, MoovMoney, MtnMomo, Wave }

public enum PaymentStatus { Pending, Processing, Authorized, Succeeded, Failed, Cancelled, Refunded }

public enum WalletTransactionType
{
    Topup, Trip, Refund, TransferIn, TransferOut,
    Withdrawal, Bonus, Adjustment, CashSettlement
}

public enum DocumentType
{
    GovernmentId, DriversLicense, VehicleRegistration,
    Insurance, GuarantorId, Selfie, VehiclePhoto
}

public enum VehicleType { Bike, Car, Tricycle, Van }

public enum SosType { Accident, Harassment, Medical, Other }

public enum SosStatus { Active, Resolved, Escalated }

public enum NotificationGroup { Trip, Payment, Promo, System }

public enum MessageType { Text, Image, Audio, System }

public enum ConversationKind { Trip, Support }

public enum TicketStatus { Open, Pending, Resolved, Closed }

public enum TicketPriority { Low, Medium, High, Urgent }

public enum OtpPurpose { Login, Register, VerifyPhone, VerifyEmail }

public enum CoRideRequestStatus { Searching, Matched, Cancelled, Expired }

public enum OnboardingStepKey
{
    Personal, Identity, License, Vehicle,
    Insurance, Guarantor, Payout, Selfie
}

public enum OnboardingStepStatus { Empty, Submitted, Approved, Rejected }

// Admin
public enum StaffStatus { Active, Suspended, Blocked }

public enum Market { CI, NG }

public enum AuditAction
{
    Login, Logout, PasswordChange,
    TripRefund, TripAdjust, TripReopen,
    WalletAdjust, WalletFreeze,
    PayoutApprove, PayoutReject, PayoutWriteOff,
    KycApprove, KycReject, KycStepApprove, KycStepReject,
    RiderSuspend, RiderBlock, RiderUnblock, RiderDelete,
    DriverSuspend, DriverBlock, DriverUnblock,
    StaffInvite, StaffRoleChange, StaffPermissionChange,
    StaffPasswordReset, Staff2faReset, StaffSuspend, StaffBlock, StaffReinstate,
    SosAcknowledge, SosResolve, SosEscalate,
    FareRuleChange, CommissionChange, SurgeOverride,
    BroadcastSend, DocumentAccess, LocationHistoryAccess,
    ConfigChange, FeatureFlagChange, MaintenanceChange, IntegrationChange,
    TwoFaEnroll, TwoFaVerify, PasswordReset, InviteAccept,
    RiderFlag, RiderLogout, DriverLogout, DriverPerformanceReset, PasswordResetLinkSent,
    PaymentRefund, PaymentRetry, PaymentReplayWebhook, PaymentMarkReconciled,
    CashSettlementRecord,
    CouponChange, IncentiveChange, ReferralConfigChange,
    SupportTicketAction, BroadcastAction
}
