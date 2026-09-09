namespace Izigo.Application.Features.Config.Dtos;

// ── GET /config ───────────────────────────────────────────────────────────────
// Exact field names from the contract
public record ConfigDto(
    MinAppVersionDto MinAppVersion,
    bool MaintenanceMode,
    string? MaintenanceMessage,
    string Currency,
    string CurrencySymbol,
    string Country,
    MapCenterDto DefaultMapCenter,
    string[] VerticalsEnabled,
    string[] PaymentMethods,
    string SupportPhone,
    string SosPhone,
    object CancellationPolicy,
    bool ReferralEnabled,
    bool WalletEnabled,
    bool TippingEnabled
);

public record MinAppVersionDto(string Android, string Ios);

public record MapCenterDto(double Lat, double Lng);

// ── GET /config/version-check ─────────────────────────────────────────────────
public record VersionCheckDto(
    string Action,      // none | soft_update | force_update | maintenance
    string? StoreUrl
);

// ── GET /zones ────────────────────────────────────────────────────────────────
public record ZoneDto(
    string Id,
    string Name,
    string Market,
    string PolygonGeoJson,
    double CenterLat,
    double CenterLng,
    string Status,
    string[] VerticalsEnabled
);

// ── GET /pages/{slug} ─────────────────────────────────────────────────────────
public record PageDto(
    string Slug,
    string ContentFormat,   // html | markdown
    string Content,
    DateTime UpdatedAt
);

// ── GET /languages ────────────────────────────────────────────────────────────
public record LanguageDto(string Code, string Name, string NativeName);

// ── GET /onboarding-slides ────────────────────────────────────────────────────
public record OnboardingSlideDto(
    string ImageUrl,
    string Title,
    string? Subtitle,
    int Order
);

// ── GET /banners ──────────────────────────────────────────────────────────────
public record BannerDto(
    string ImageUrl,
    string? DeepLink,
    string Placement   // home | wallet
);

// ── GET /service-classes ──────────────────────────────────────────────────────
public record ServiceClassDto(
    string Code,        // zen_car | zen_bike | zen_coride | package_small | package_large
    string Name,
    string IconKey,
    int SeatCapacity,
    string Description
);
