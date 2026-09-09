using System.Text.Json;
using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Config.Dtos;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Config.Queries;

// ── GET /config ───────────────────────────────────────────────────────────────

public record GetConfigQuery(string Market) : IRequest<ConfigDto>;

public class GetConfigHandler(IApplicationDbContext db) : IRequestHandler<GetConfigQuery, ConfigDto>
{
    public async Task<ConfigDto> Handle(GetConfigQuery req, CancellationToken ct)
    {
        var cfg = await db.PlatformConfigs
            .FirstOrDefaultAsync(c => c.Market == req.Market, ct)
            ?? await db.PlatformConfigs.FirstOrDefaultAsync(ct)
            ?? DefaultConfig(req.Market);

        var verticals = JsonSerializer.Deserialize<string[]>(cfg.VerticalsEnabledJson)
                        ?? ["ride", "co_ride", "package"];

        var paymentMethods = JsonSerializer.Deserialize<string[]>(cfg.PaymentMethodsJson)
                             ?? ["cash", "wallet"];

        object cancellationPolicy;
        try { cancellationPolicy = JsonSerializer.Deserialize<object>(cfg.CancellationPolicyJson)!; }
        catch { cancellationPolicy = new { }; }

        return new ConfigDto(
            MinAppVersion: new MinAppVersionDto(cfg.MinAppVersionAndroid, cfg.MinAppVersionIos),
            MaintenanceMode: cfg.MaintenanceModeRider || cfg.MaintenanceModeDriver,
            MaintenanceMessage: cfg.MaintenanceMessage,
            Currency: cfg.Currency,
            CurrencySymbol: cfg.CurrencySymbol,
            Country: cfg.Country,
            DefaultMapCenter: new MapCenterDto(
                (double)cfg.DefaultMapCenterLat, (double)cfg.DefaultMapCenterLng),
            VerticalsEnabled: verticals,
            PaymentMethods: paymentMethods,
            SupportPhone: cfg.SupportPhone,
            SosPhone: cfg.SosPhone,
            CancellationPolicy: cancellationPolicy,
            ReferralEnabled: cfg.ReferralEnabled,
            WalletEnabled: cfg.WalletEnabled,
            TippingEnabled: cfg.TippingEnabled
        );
    }

    private static Domain.Entities.PlatformConfig DefaultConfig(string market) => new()
    {
        Market = market
    };
}

// ── GET /config/version-check ─────────────────────────────────────────────────

public record GetVersionCheckQuery(string Platform, string Version, string Market)
    : IRequest<VersionCheckDto>;

public class GetVersionCheckHandler(IApplicationDbContext db)
    : IRequestHandler<GetVersionCheckQuery, VersionCheckDto>
{
    public async Task<VersionCheckDto> Handle(GetVersionCheckQuery req, CancellationToken ct)
    {
        var cfg = await db.PlatformConfigs
            .FirstOrDefaultAsync(c => c.Market == req.Market, ct)
            ?? await db.PlatformConfigs.FirstOrDefaultAsync(ct);

        if (cfg == null) return new VersionCheckDto("none", null);

        // Maintenance takes precedence
        if (cfg.MaintenanceModeRider || cfg.MaintenanceModeDriver)
            return new VersionCheckDto("maintenance", null);

        var (minVersion, storeUrl) = req.Platform.ToLower() == "ios"
            ? (cfg.MinAppVersionIos, cfg.IosStoreUrl)
            : (cfg.MinAppVersionAndroid, cfg.AndroidStoreUrl);

        var latestVersion = req.Platform.ToLower() == "ios"
            ? cfg.LatestAppVersionIos
            : cfg.LatestAppVersionAndroid;

        var action = CompareVersions(req.Version, minVersion) < 0
            ? "force_update"
            : CompareVersions(req.Version, latestVersion) < 0
                ? "soft_update"
                : "none";

        return new VersionCheckDto(action, action != "none" ? storeUrl : null);
    }

    // Simple semver comparison: returns negative if a < b
    private static int CompareVersions(string a, string b)
    {
        var aParts = a.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
        var bParts = b.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
        var len = Math.Max(aParts.Length, bParts.Length);

        for (var i = 0; i < len; i++)
        {
            var av = i < aParts.Length ? aParts[i] : 0;
            var bv = i < bParts.Length ? bParts[i] : 0;
            if (av != bv) return av.CompareTo(bv);
        }
        return 0;
    }
}

// ── GET /zones ────────────────────────────────────────────────────────────────

public record GetZonesQuery(string Market) : IRequest<List<ZoneDto>>;

public class GetZonesHandler(IApplicationDbContext db) : IRequestHandler<GetZonesQuery, List<ZoneDto>>
{
    public async Task<List<ZoneDto>> Handle(GetZonesQuery req, CancellationToken ct)
        => await db.Zones
            .Where(z => z.Market == req.Market || string.IsNullOrEmpty(req.Market))
            .OrderBy(z => z.Name)
            .Select(z => new ZoneDto(
                z.Id,
                z.Name,
                z.Market,
                z.PolygonGeoJson,
                (double)z.CenterLat,
                (double)z.CenterLng,
                z.Status,
                z.VerticalsEnabled.Select(v => v.ToString().ToLower()).ToArray()
            ))
            .ToListAsync(ct);
}

// ── GET /pages/{slug} ─────────────────────────────────────────────────────────

public record GetPageQuery(string Slug, string Language, string Market) : IRequest<PageDto>;

public class GetPageHandler(IApplicationDbContext db) : IRequestHandler<GetPageQuery, PageDto>
{
    private static readonly string[] AllowedSlugs =
        ["terms", "privacy", "about", "driver-terms", "cancellation-policy", "referral-terms"];

    public async Task<PageDto> Handle(GetPageQuery req, CancellationToken ct)
    {
        if (!AllowedSlugs.Contains(req.Slug))
            throw new KeyNotFoundException($"Page '{req.Slug}' not found.");

        // Try exact language match first, then fall back to 'fr'
        var page = await db.AppPages
            .Where(p => p.Slug == req.Slug &&
                        (p.Market == req.Market || p.Market == "") &&
                        p.Language == req.Language)
            .FirstOrDefaultAsync(ct)
            ?? await db.AppPages
                .Where(p => p.Slug == req.Slug &&
                            (p.Market == req.Market || p.Market == ""))
                .FirstOrDefaultAsync(ct);

        if (page == null)
            throw new KeyNotFoundException($"Page '{req.Slug}' not found.");

        return new PageDto(page.Slug, page.ContentFormat, page.Content, page.UpdatedAt);
    }
}

// ── GET /languages ────────────────────────────────────────────────────────────

public record GetLanguagesQuery : IRequest<List<LanguageDto>>;

public class GetLanguagesHandler : IRequestHandler<GetLanguagesQuery, List<LanguageDto>>
{
    // Seeded list — add more without a release once the /languages endpoint is wired
    private static readonly List<LanguageDto> Languages =
    [
        new("fr", "French", "Français"),
        new("en", "English", "English")
    ];

    public Task<List<LanguageDto>> Handle(GetLanguagesQuery req, CancellationToken ct)
        => Task.FromResult(Languages);
}

// ── GET /onboarding-slides ────────────────────────────────────────────────────

public record GetOnboardingSlidesQuery(string Audience, string Language, string Market)
    : IRequest<List<OnboardingSlideDto>>;

public class GetOnboardingSlidesHandler(IApplicationDbContext db)
    : IRequestHandler<GetOnboardingSlidesQuery, List<OnboardingSlideDto>>
{
    public async Task<List<OnboardingSlideDto>> Handle(GetOnboardingSlidesQuery req, CancellationToken ct)
        => await db.OnboardingSlides
            .Where(s => s.IsActive &&
                        s.Audience == req.Audience &&
                        (s.Market == req.Market || s.Market == "") &&
                        (s.Language == req.Language || s.Language == "fr"))
            .OrderBy(s => s.Order)
            .Select(s => new OnboardingSlideDto(s.ImageUrl, s.Title, s.Subtitle, s.Order))
            .ToListAsync(ct);
}

// ── GET /banners ──────────────────────────────────────────────────────────────

public record GetBannersQuery(string Market, string? Placement) : IRequest<List<BannerDto>>;

public class GetBannersHandler(IApplicationDbContext db)
    : IRequestHandler<GetBannersQuery, List<BannerDto>>
{
    public async Task<List<BannerDto>> Handle(GetBannersQuery req, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return await db.Banners
            .Where(b => b.IsActive &&
                        (b.Market == req.Market || b.Market == "") &&
                        (req.Placement == null || b.Placement == req.Placement) &&
                        (b.StartsAt == null || b.StartsAt <= now) &&
                        (b.EndsAt == null || b.EndsAt >= now))
            .OrderBy(b => b.Order)
            .Select(b => new BannerDto(b.ImageUrl, b.DeepLink, b.Placement))
            .ToListAsync(ct);
    }
}

// ── GET /service-classes ──────────────────────────────────────────────────────

public record GetServiceClassesQuery : IRequest<List<ServiceClassDto>>;

public class GetServiceClassesHandler : IRequestHandler<GetServiceClassesQuery, List<ServiceClassDto>>
{
    // Static catalogue matching the contract's exact code values
    private static readonly List<ServiceClassDto> ServiceClasses =
    [
        new("zen_car",      "Zen Car",      "car",     4, "Comfortable sedan for up to 4 passengers"),
        new("zen_bike",     "Zen Bike",     "bike",    1, "Quick motorbike for solo riders"),
        new("zen_coride",   "Zen Co-Ride",  "group",   6, "Shared trip along a set route"),
        new("package_small","Small Package","package", 0, "Envelopes and small parcels up to 5 kg"),
        new("package_large","Large Package","box",     0, "Larger items up to 25 kg")
    ];

    public Task<List<ServiceClassDto>> Handle(GetServiceClassesQuery req, CancellationToken ct)
        => Task.FromResult(ServiceClasses);
}
