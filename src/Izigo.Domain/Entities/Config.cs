using Izigo.Domain.Common;

namespace Izigo.Domain.Entities;

/// <summary>
/// Single row per market. The admin PUT /admin/config/* endpoints update this.
/// The app GET /config reads from it on every launch.
/// </summary>
public class PlatformConfig : BaseEntity
{
    public string Market { get; set; } = "ci";   // ci | ng

    // App identity
    public string Currency { get; set; } = "XOF";
    public string CurrencySymbol { get; set; } = "F CFA";
    public string Country { get; set; } = "CI";
    public decimal DefaultMapCenterLat { get; set; } = 5.3599m;
    public decimal DefaultMapCenterLng { get; set; } = -3.9970m;

    // Enabled verticals & payment methods (JSON arrays)
    public string VerticalsEnabledJson { get; set; } = "[\"ride\",\"co_ride\",\"package\"]";
    public string PaymentMethodsJson { get; set; } = "[\"cash\",\"wallet\",\"orange_money\",\"moov_money\",\"wave\"]";

    // Contact
    public string SupportPhone { get; set; } = string.Empty;
    public string SosPhone { get; set; } = string.Empty;

    // Policy summary surfaced to the UI (JSON object or markdown string)
    public string CancellationPolicyJson { get; set; } = "{}";

    // Feature toggles
    public bool ReferralEnabled { get; set; } = true;
    public bool WalletEnabled { get; set; } = true;
    public bool TippingEnabled { get; set; }

    // App version gates — compared client-side by version-check endpoint
    public string MinAppVersionIos { get; set; } = "1.0.0";
    public string MinAppVersionAndroid { get; set; } = "1.0.0";
    public string LatestAppVersionIos { get; set; } = "1.0.0";
    public string LatestAppVersionAndroid { get; set; } = "1.0.0";
    public string? IosStoreUrl { get; set; }
    public string? AndroidStoreUrl { get; set; }

    // Referral configuration (JSON: { reward_referrer, reward_referred, qualification_trips, cap })
    public string? ReferralConfigJson { get; set; }

    // Maintenance
    public bool MaintenanceModeRider { get; set; }
    public bool MaintenanceModeDriver { get; set; }
    public string? MaintenanceMessage { get; set; }

    // Feature flags (JSON object: { "key": { "enabled": true, "rollout_pct": 100 } })
    public string FeatureFlagsJson { get; set; } =
        "{\"co_ride_enabled\":{\"enabled\":true},\"package_enabled\":{\"enabled\":true}," +
        "\"tipping\":{\"enabled\":false},\"wallet_transfer\":{\"enabled\":true}," +
        "\"instant_payout\":{\"enabled\":false},\"referrals\":{\"enabled\":true}," +
        "\"masked_calls\":{\"enabled\":true},\"biometric_login\":{\"enabled\":true}," +
        "\"scheduled_rides\":{\"enabled\":false}}";

    // Dispatch config (also editable via PUT /admin/config/dispatch)
    public int OfferTimeoutSeconds { get; set; } = 15;
    public int SearchRadiusM { get; set; } = 5000;
    public string DispatchStrategy { get; set; } = "broadcast";   // broadcast | sequential
    public int MaxConcurrentOffers { get; set; } = 5;
    public int LocationPingOnTripSeconds { get; set; } = 5;
    public int LocationPingIdleSeconds { get; set; } = 20;
}

/// <summary>Slug-based content pages served via GET /pages/{slug}.</summary>
public class AppPage : BaseEntity
{
    public string Slug { get; set; } = string.Empty;   // terms | privacy | about | driver-terms | cancellation-policy
    public string Market { get; set; } = string.Empty;
    public string Language { get; set; } = "fr";
    public string ContentFormat { get; set; } = "markdown";   // html | markdown
    public string Content { get; set; } = string.Empty;
    public new DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>First-run onboarding carousel slide.</summary>
public class OnboardingSlide : BaseEntity
{
    public string Audience { get; set; } = "rider";   // rider | driver
    public string ImageUrl { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public int Order { get; set; }
    public bool IsActive { get; set; } = true;
    public string Market { get; set; } = string.Empty;
    public string Language { get; set; } = "fr";
}

/// <summary>Home / wallet promo banner returned by GET /banners.</summary>
public class Banner : BaseEntity
{
    public string ImageUrl { get; set; } = string.Empty;
    public string? DeepLink { get; set; }
    public string Placement { get; set; } = "home";   // home | wallet
    public bool IsActive { get; set; } = true;
    public string Market { get; set; } = string.Empty;
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public int Order { get; set; }
}
