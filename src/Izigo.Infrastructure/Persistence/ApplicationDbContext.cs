using Izigo.Application.Common.Interfaces;
using Izigo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Infrastructure.Persistence;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : DbContext(options), IApplicationDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<DriverProfile> DriverProfiles => Set<DriverProfile>();
    public DbSet<UserDevice> UserDevices => Set<UserDevice>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<SavedPlace> SavedPlaces => Set<SavedPlace>();
    public DbSet<EmergencyContact> EmergencyContacts => Set<EmergencyContact>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<BiometricToken> BiometricTokens => Set<BiometricToken>();
    public DbSet<OtpRecord> OtpRecords => Set<OtpRecord>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<DriverDocument> DriverDocuments => Set<DriverDocument>();
    public DbSet<DriverOnboarding> DriverOnboardings => Set<DriverOnboarding>();
    public DbSet<DriverLocationPoint> DriverLocationPoints => Set<DriverLocationPoint>();
    public DbSet<PayoutMethod> PayoutMethods => Set<PayoutMethod>();
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<TripStateHistory> TripStateHistories => Set<TripStateHistory>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<CoRideListing> CoRideListings => Set<CoRideListing>();
    public DbSet<CoRideBooking> CoRideBookings => Set<CoRideBooking>();
    public DbSet<Package> Packages => Set<Package>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();
    public DbSet<DriverWallet> DriverWallets => Set<DriverWallet>();
    public DbSet<DriverWalletTransaction> DriverWalletTransactions => Set<DriverWalletTransaction>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();
    public DbSet<Payout> Payouts => Set<Payout>();
    public DbSet<PayoutAttempt> PayoutAttempts => Set<PayoutAttempt>();
    public DbSet<UserPaymentMethod> UserPaymentMethods => Set<UserPaymentMethod>();
    public DbSet<FareRule> FareRules => Set<FareRule>();
    public DbSet<CommissionConfig> CommissionConfigs => Set<CommissionConfig>();
    public DbSet<SurgeZone> SurgeZones => Set<SurgeZone>();
    public DbSet<Zone> Zones => Set<Zone>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
    public DbSet<SupportTicketMessage> SupportTicketMessages => Set<SupportTicketMessage>();
    public DbSet<SosIncident> SosIncidents => Set<SosIncident>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<Broadcast> Broadcasts => Set<Broadcast>();
    public DbSet<Incentive> Incentives => Set<Incentive>();
    public DbSet<BackgroundJob> BackgroundJobs => Set<BackgroundJob>();
    public DbSet<CancellationReason> CancellationReasons => Set<CancellationReason>();
    public DbSet<CoRideRequest> CoRideRequests => Set<CoRideRequest>();
    public DbSet<DeviceDiagnostic> DeviceDiagnostics => Set<DeviceDiagnostic>();
    public DbSet<PlatformConfig> PlatformConfigs => Set<PlatformConfig>();
    public DbSet<AppPage> AppPages => Set<AppPage>();
    public DbSet<OnboardingSlide> OnboardingSlides => Set<OnboardingSlide>();
    public DbSet<Banner> Banners => Set<Banner>();
    public DbSet<Staff> Staff => Set<Staff>();
    public DbSet<StaffRefreshToken> StaffRefreshTokens => Set<StaffRefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<TwoFaChallenge> TwoFaChallenges => Set<TwoFaChallenge>();
    public DbSet<TwoFaRecoveryCode> TwoFaRecoveryCodes => Set<TwoFaRecoveryCode>();
    public DbSet<StaffInvite> StaffInvites => Set<StaffInvite>();
    public DbSet<StaffPasswordReset> StaffPasswordResets => Set<StaffPasswordReset>();
    public DbSet<DispatchConfig> DispatchConfigs => Set<DispatchConfig>();
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
    public DbSet<Integration> Integrations => Set<Integration>();
    public DbSet<StaffRoleCustomization> StaffRoleCustomizations => Set<StaffRoleCustomization>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<Domain.Common.BaseEntity>())
        {
            if (entry.State == EntityState.Modified)
                entry.Entity.UpdatedAt = DateTime.UtcNow;
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
