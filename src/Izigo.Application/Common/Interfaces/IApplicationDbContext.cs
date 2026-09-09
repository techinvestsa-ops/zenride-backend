using Izigo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    // Users & Auth
    DbSet<User> Users { get; }
    DbSet<DriverProfile> DriverProfiles { get; }
    DbSet<UserDevice> UserDevices { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<SavedPlace> SavedPlaces { get; }
    DbSet<EmergencyContact> EmergencyContacts { get; }
    DbSet<UserPreference> UserPreferences { get; }
    DbSet<BiometricToken> BiometricTokens { get; }
    DbSet<OtpRecord> OtpRecords { get; }
    DbSet<IdempotencyRecord> IdempotencyRecords { get; }

    // Driver
    DbSet<Vehicle> Vehicles { get; }
    DbSet<DriverDocument> DriverDocuments { get; }
    DbSet<DriverOnboarding> DriverOnboardings { get; }
    DbSet<DriverLocationPoint> DriverLocationPoints { get; }
    DbSet<PayoutMethod> PayoutMethods { get; }

    // Trips & Verticals
    DbSet<Trip> Trips { get; }
    DbSet<TripStateHistory> TripStateHistories { get; }
    DbSet<Quote> Quotes { get; }
    DbSet<CoRideListing> CoRideListings { get; }
    DbSet<CoRideBooking> CoRideBookings { get; }
    DbSet<Package> Packages { get; }

    // Finance
    DbSet<Wallet> Wallets { get; }
    DbSet<WalletTransaction> WalletTransactions { get; }
    DbSet<DriverWallet> DriverWallets { get; }
    DbSet<DriverWalletTransaction> DriverWalletTransactions { get; }
    DbSet<Payment> Payments { get; }
    DbSet<WebhookEvent> WebhookEvents { get; }
    DbSet<Payout> Payouts { get; }
    DbSet<PayoutAttempt> PayoutAttempts { get; }
    DbSet<UserPaymentMethod> UserPaymentMethods { get; }
    DbSet<FareRule> FareRules { get; }
    DbSet<CommissionConfig> CommissionConfigs { get; }
    DbSet<SurgeZone> SurgeZones { get; }

    // Platform
    DbSet<Zone> Zones { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<Conversation> Conversations { get; }
    DbSet<Message> Messages { get; }
    DbSet<SupportTicket> SupportTickets { get; }
    DbSet<SupportTicketMessage> SupportTicketMessages { get; }
    DbSet<SosIncident> SosIncidents { get; }
    DbSet<Coupon> Coupons { get; }
    DbSet<Broadcast> Broadcasts { get; }
    DbSet<Incentive> Incentives { get; }
    DbSet<BackgroundJob> BackgroundJobs { get; }

    // Rides / Co-Ride extras
    DbSet<CancellationReason> CancellationReasons { get; }
    DbSet<CoRideRequest> CoRideRequests { get; }
    DbSet<DeviceDiagnostic> DeviceDiagnostics { get; }

    // Config
    DbSet<PlatformConfig> PlatformConfigs { get; }
    DbSet<AppPage> AppPages { get; }
    DbSet<OnboardingSlide> OnboardingSlides { get; }
    DbSet<Banner> Banners { get; }

    // Admin
    DbSet<Staff> Staff { get; }
    DbSet<StaffRefreshToken> StaffRefreshTokens { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<TwoFaChallenge> TwoFaChallenges { get; }
    DbSet<TwoFaRecoveryCode> TwoFaRecoveryCodes { get; }
    DbSet<StaffInvite> StaffInvites { get; }
    DbSet<StaffPasswordReset> StaffPasswordResets { get; }
    DbSet<DispatchConfig> DispatchConfigs { get; }
    DbSet<FeatureFlag> FeatureFlags { get; }
    DbSet<Integration> Integrations { get; }
    DbSet<StaffRoleCustomization> StaffRoleCustomizations { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
