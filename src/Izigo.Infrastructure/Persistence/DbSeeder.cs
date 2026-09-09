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
                Currency             = market == "ci" ? "XOF" : "NGN",
                CurrencySymbol       = market == "ci" ? "F CFA" : "₦",
                Country              = market == "ci" ? "CI" : "NG",
                DefaultMapCenterLat  = market == "ci" ? 5.3599m  : 6.4541m,
                DefaultMapCenterLng  = market == "ci" ? -3.9970m : 3.3947m,
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

    // ── Fare Rules (CI market only) ───────────────────────────────────────────
    // NG rules are seeded separately once CreateQuoteHandler filters by market (TODO 2.3).

    private static async Task SeedFareRulesAsync(ApplicationDbContext db, ILogger logger)
    {
        if (await db.FareRules.AnyAsync(r => r.Market == "ci"))
            return;

        var rules = new[]
        {
            // ZenCar — standard 4-seat sedan for Abidjan
            new FareRule
            {
                ServiceClass    = ServiceClass.ZenCar,
                Base            = 500,    // XOF — flag-fall
                PerKm           = 250,    // XOF / km
                PerMin          = 15,     // XOF / min
                Minimum         = 1200,   // XOF — minimum fare
                WaitingPerMin   = 20,     // XOF / min while waiting at pickup
                CancellationFee = 500,    // XOF — charged if rider cancels after arrival
                IsActive        = true,
                Version         = 1,
                Market          = "ci"
            },
            // ZenBike — motorbike, faster for short hops
            new FareRule
            {
                ServiceClass    = ServiceClass.ZenBike,
                Base            = 300,
                PerKm           = 180,
                PerMin          = 10,
                Minimum         = 800,
                WaitingPerMin   = 15,
                CancellationFee = 300,
                IsActive        = true,
                Version         = 1,
                Market          = "ci"
            },
            // ZenCoRide — shared ride, cheaper per-seat rate
            new FareRule
            {
                ServiceClass    = ServiceClass.ZenCoRide,
                Base            = 250,
                PerKm           = 190,
                PerMin          = 10,
                Minimum         = 600,
                WaitingPerMin   = 10,
                CancellationFee = 200,
                IsActive        = true,
                Version         = 1,
                Market          = "ci"
            },
            // PackageSmall — parcels up to ~5 kg
            new FareRule
            {
                ServiceClass    = ServiceClass.PackageSmall,
                Base            = 400,
                PerKm           = 200,
                PerMin          = 12,
                Minimum         = 900,
                WaitingPerMin   = 0,     // no waiting fee for deliveries
                CancellationFee = 0,     // no cancellation fee for packages
                IsActive        = true,
                Version         = 1,
                Market          = "ci"
            },
            // PackageLarge — bulky items, van or large bike
            new FareRule
            {
                ServiceClass    = ServiceClass.PackageLarge,
                Base            = 600,
                PerKm           = 280,
                PerMin          = 12,
                Minimum         = 1500,
                WaitingPerMin   = 0,
                CancellationFee = 0,
                IsActive        = true,
                Version         = 1,
                Market          = "ci"
            }
        };

        db.FareRules.AddRange(rules);
        await db.SaveChangesAsync();
        logger.LogInformation("[Seed] Created {Count} FareRules for market=ci", rules.Length);
    }

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
