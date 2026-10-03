using Izigo.Application.Common.Interfaces;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Izigo.Infrastructure.Persistence;

/// <summary>
/// Seeds the minimum data required for the system to be usable on first boot.
/// All methods are idempotent — safe to call on every startup.
///
/// Seeded data:
///   1. PlatformConfig (CI + NG)
///   2. Super-admin staff account
///   3. FareRules for CI market (ZenCar, ZenBike, ZenCoRide, PackageSmall, PackageLarge)
///   4. DispatchConfig for CI + NG
///   5. CommissionConfig for CI + NG
///   6. FeatureFlags for CI + NG
///
/// Note: NG FareRules are intentionally not seeded here.
/// The CreateQuoteHandler does not yet filter rules by market (TODO 2.3).
/// Seed NG rules when market assignment is implemented to avoid ambiguous rule lookups.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope  = services.CreateScope();
        var db           = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var config       = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var hasher       = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var logger       = scope.ServiceProvider.GetRequiredService<ILogger<ApplicationDbContext>>();

        await db.Database.MigrateAsync();

        await SeedPlatformConfigAsync(db, logger);
        await SeedSuperAdminAsync(db, config, hasher, logger);
        await SeedFareRulesAsync(db, logger);
        await ApplyGuineaFaresAsync(db, logger);
        await SeedDispatchConfigsAsync(db, logger);
        await SeedCommissionConfigsAsync(db, logger);
        await SeedFeatureFlagsAsync(db, logger);
    }

    // ── PlatformConfig ────────────────────────────────────────────────────────

    private static async Task SeedPlatformConfigAsync(ApplicationDbContext db, ILogger logger)
    {
        foreach (var market in new[] { "ci", "ng" })
        {
            if (await db.PlatformConfigs.AnyAsync(c => c.Market == market))
                continue;

            var cfg = new PlatformConfig
            {
                Market               = market,
                Currency             = market == "ci" ? "GNF" : "NGN",
                CurrencySymbol       = market == "ci" ? "GNF" : "₦",
                Country              = market == "ci" ? "GN" : "NG",
                DefaultMapCenterLat  = market == "ci" ? 9.6412m  : 6.4541m,
                DefaultMapCenterLng  = market == "ci" ? -13.5784m : 3.3947m,
                SupportPhone         = "",
                SosPhone             = "",
                ReferralEnabled      = true,
                WalletEnabled        = true,
                MaintenanceModeRider  = false,
                MaintenanceModeDriver = false
            };
            db.PlatformConfigs.Add(cfg);
            logger.LogInformation("[Seed] Created PlatformConfig for market={Market}", market);
        }

        await db.SaveChangesAsync();
    }

    // ── Super admin account ───────────────────────────────────────────────────

    private static async Task SeedSuperAdminAsync(ApplicationDbContext db,
        IConfiguration config, IPasswordHasher hasher, ILogger logger)
    {
        if (await db.Staff.AnyAsync(s => s.RoleKey == "super_admin"))
            return;

        var email    = config["Seed:SuperAdminEmail"]    ?? "admin@zenride.app";
        var name     = config["Seed:SuperAdminName"]     ?? "Super Admin";
        var password = config["Seed:SuperAdminPassword"] ?? "Admin@Zenride2025!";

        var staff = new Staff
        {
            Name               = name,
            Email              = email,
            RoleKey            = "super_admin",
            Markets            = ["ci", "ng"],
            Status             = StaffStatus.Active,
            TwoFaEnabled       = false,
            MustChangePassword = true,
            PasswordHash       = hasher.Hash(password)
        };
        db.Staff.Add(staff);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "[Seed] Created super admin: email={Email} — CHANGE PASSWORD ON FIRST LOGIN", email);

        Console.WriteLine($"""
            ╔══════════════════════════════════════════════╗
            ║         INITIAL SUPER ADMIN CREATED          ║
            ║  Email:    {email,-34}║
            ║  Password: {password,-34}║
            ║  !! Change this password immediately !!      ║
            ╚══════════════════════════════════════════════╝
            """);
    }

    // Prices are in Guinean francs and sit under a private taxi in Conakry
    // (about 8,000 GNF per km, and 30,000–70,000 GNF for a short hop).

    private static async Task SeedFareRulesAsync(ApplicationDbContext db, ILogger logger)
    {
        if (await db.FareRules.AnyAsync(r => r.Market == "ci"))
            return;

        foreach (var rate in GuineaRates)
            db.FareRules.Add(NewGuineaRule(rate, version: 1));

        await db.SaveChangesAsync();
        logger.LogInformation("[Seed] Created Guinea fare rules for market=ci");
    }

    /// <summary>
    /// Replaces the old CFA-sized rates once. After that, admin edits stick
    /// because a car rate of 3,000 GNF per km is left alone.
    /// </summary>
    private static async Task ApplyGuineaFaresAsync(ApplicationDbContext db, ILogger logger)
    {
        var car = await db.FareRules
            .Where(r => r.Market == "ci" && r.IsActive && r.ServiceClass == ServiceClass.ZenCar)
            .OrderByDescending(r => r.Version)
            .FirstOrDefaultAsync();
        if (car is not null && car.PerKm >= 1_000)
            return;

        var config = await db.PlatformConfigs.FirstOrDefaultAsync(c => c.Market == "ci");
        if (config is not null)
        {
            config.Currency = "GNF";
            config.CurrencySymbol = "GNF";
            config.Country = "GN";
            config.DefaultMapCenterLat = 9.6412m;
            config.DefaultMapCenterLng = -13.5784m;
        }

        var active = await db.FareRules.Where(r => r.Market == "ci" && r.IsActive).ToListAsync();
        foreach (var rate in GuineaRates)
        {
            var current = active.FirstOrDefault(r => r.ServiceClass == rate.ServiceClass);
            if (current is null)
            {
                db.FareRules.Add(NewGuineaRule(rate, version: 1));
                continue;
            }

            current.Base = rate.Base;
            current.PerKm = rate.PerKm;
            current.PerMin = 0;
            current.Minimum = rate.Minimum;
            current.WaitingPerMin = rate.WaitingPerMin;
            current.WaitGraceMin = 10;
            current.CancellationFee = rate.CancellationFee;
            current.TrafficDelayMin = 15;
            current.TrafficPercent = 8m;
        }

        await db.SaveChangesAsync();
        logger.LogInformation("[Seed] Set Conakry fares in GNF, under local taxi rates");
    }

    private static FareRule NewGuineaRule(GuineaRate rate, int version) => new()
    {
        ServiceClass = rate.ServiceClass,
        Base = rate.Base,
        PerKm = rate.PerKm,
        PerMin = 0,
        Minimum = rate.Minimum,
        WaitingPerMin = rate.WaitingPerMin,
        WaitGraceMin = 10,
        CancellationFee = rate.CancellationFee,
        TrafficDelayMin = 15,
        TrafficPercent = 8m,
        IsActive = true,
        Version = version,
        Market = "ci"
    };

    private readonly record struct GuineaRate(
        ServiceClass ServiceClass, long Base, long PerKm, long Minimum,
        long WaitingPerMin, long CancellationFee);

    private static readonly GuineaRate[] GuineaRates =
    [
        new(ServiceClass.ZenCar,        2_000, 3_000, 7_000, 200, 2_000),
        new(ServiceClass.ZenBike,       1_000, 1_800, 4_000, 100, 1_000),
        new(ServiceClass.ZenCoRide,       500,   800, 2_000,  50,   500),
        new(ServiceClass.PackageSmall,  1_500, 2_200, 5_000,   0,     0),
        new(ServiceClass.PackageLarge,  2_500, 3_200, 8_000,   0,     0),
    ];

    // ── Dispatch Config ───────────────────────────────────────────────────────

    private static async Task SeedDispatchConfigsAsync(ApplicationDbContext db, ILogger logger)
    {
        foreach (var market in new[] { "ci", "ng" })
        {
            if (await db.DispatchConfigs.AnyAsync(d => d.Market == market))
                continue;

            db.DispatchConfigs.Add(new DispatchConfig
            {
                Market               = market,
                OfferTimeoutSeconds  = 15,
                SearchRadiusM        = 3000,
                Strategy             = "broadcast",
                MaxConcurrentOffers  = 3,
                LocationPingOnTripS  = 5,
                LocationPingIdleS    = 20
            });
            logger.LogInformation("[Seed] Created DispatchConfig for market={Market}", market);
        }

        await db.SaveChangesAsync();
    }

    // ── Commission Config ─────────────────────────────────────────────────────

    private static async Task SeedCommissionConfigsAsync(ApplicationDbContext db, ILogger logger)
    {
        foreach (var market in new[] { "ci", "ng" })
        {
            if (await db.CommissionConfigs.AnyAsync(c => c.Market == market))
                continue;

            db.CommissionConfigs.Add(new CommissionConfig
            {
                CommissionRate       = 0.25m,   // 25% platform commission
                BonusRate            = 0.035m,  // 3.5% ChopMonie bonus pool
                BonusLabel           = "ChopMonie Bonus",
                CashSettlementCap    = market == "ci" ? 10_000 : 5_000,  // XOF / NGN daily cash cap
                VerticalOverridesJson = "{}",
                Version              = 1,
                Market               = market
            });
            logger.LogInformation("[Seed] Created CommissionConfig for market={Market}", market);
        }

        await db.SaveChangesAsync();
    }

    // ── Feature Flags ─────────────────────────────────────────────────────────

    private static async Task SeedFeatureFlagsAsync(ApplicationDbContext db, ILogger logger)
    {
        var flags = new (string Key, string Description, bool Enabled)[]
        {
            ("co_ride_enabled",   "Enable Co-Ride (shared trips) vertical",           true),
            ("package_enabled",   "Enable Package Delivery vertical",                  true),
            ("food_enabled",      "Enable Food Delivery vertical (future)",            false),
            ("scheduled_rides",   "Allow riders to schedule trips in advance",         false),
            ("tipping",           "Allow riders to tip drivers after a trip",           false),
            ("wallet_transfer",   "Allow wallet-to-wallet transfers between riders",   true),
            ("instant_payout",    "Enable instant driver payout (higher fee)",         false),
            ("referrals",         "Enable referral programme",                         true),
            ("masked_calls",      "Mask phone numbers via proxy when calling driver",  true),
            ("biometric_login",   "Allow biometric (fingerprint/face) login",          true)
        };

        int seeded = 0;
        foreach (var market in new[] { "ci", "ng" })
        {
            foreach (var (key, description, enabled) in flags)
            {
                if (await db.FeatureFlags.AnyAsync(f => f.Key == key && f.Market == market))
                    continue;

                db.FeatureFlags.Add(new FeatureFlag
                {
                    Key         = key,
                    Description = description,
                    Enabled     = enabled,
                    RolloutPct  = 100,
                    Scope       = "global",
                    Market      = market
                });
                seeded++;
            }
        }

        if (seeded > 0)
        {
            await db.SaveChangesAsync();
            logger.LogInformation("[Seed] Created {Count} FeatureFlags", seeded);
        }
    }
}
