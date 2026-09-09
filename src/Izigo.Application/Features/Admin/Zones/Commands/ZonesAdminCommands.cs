using Izigo.Application.Common.Interfaces;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Admin.Zones.Commands;

public record ZoneCommandResult(bool Success, string? ErrorCode, object? Data = null);

// ── POST /admin/zones ─────────────────────────────────────────────────────────

public record CreateZoneCommand(string Name, string PolygonGeoJson,
    string[] Verticals, string Status,
    decimal CenterLat, decimal CenterLng,
    string Market, string StaffId, string StaffName) : IRequest<ZoneCommandResult>;

public class CreateZoneHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<CreateZoneCommand, ZoneCommandResult>
{
    private static readonly HashSet<string> ValidStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "planned", "pilot", "live" };

    public async Task<ZoneCommandResult> Handle(CreateZoneCommand cmd, CancellationToken ct)
    {
        if (!ValidStatuses.Contains(cmd.Status))
            return new(false, "INVALID_STATUS");

        var verticals = cmd.Verticals
            .Select(v => Enum.TryParse<Vertical>(v, true, out var parsed) ? (Vertical?)parsed : null)
            .Where(v => v.HasValue).Select(v => v!.Value).ToList();

        var zone = new Zone
        {
            Name             = cmd.Name,
            Market           = cmd.Market,
            PolygonGeoJson   = cmd.PolygonGeoJson,
            CenterLat        = cmd.CenterLat,
            CenterLng        = cmd.CenterLng,
            Status           = cmd.Status.ToLower(),
            VerticalsEnabled = verticals
        };
        db.Zones.Add(zone);

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.ConfigChange,
            "Zone", zone.Id, reason: "Zone created",
            after: new { zone.Name, zone.Status, zone.Market }, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null, new { id = zone.Id });
    }
}

// ── PUT /admin/zones/{id} ─────────────────────────────────────────────────────
// Flipping to "live" is what makes the app stop saying "we don't operate here".

public record UpdateZoneCommand(string ZoneId, string? Name, string? Status,
    string[]? Verticals, string? PolygonGeoJson,
    string Market, string StaffId, string StaffName) : IRequest<ZoneCommandResult>;

public class UpdateZoneHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<UpdateZoneCommand, ZoneCommandResult>
{
    public async Task<ZoneCommandResult> Handle(UpdateZoneCommand cmd, CancellationToken ct)
    {
        var zone = await db.Zones.FirstOrDefaultAsync(z => z.Id == cmd.ZoneId, ct);
        if (zone is null) return new(false, "ZONE_NOT_FOUND");

        var before = new { zone.Name, zone.Status, zone.VerticalsEnabled };

        if (cmd.Name is not null)           zone.Name           = cmd.Name;
        if (cmd.Status is not null)         zone.Status         = cmd.Status.ToLower();
        if (cmd.PolygonGeoJson is not null) zone.PolygonGeoJson = cmd.PolygonGeoJson;
        if (cmd.Verticals is not null)
        {
            zone.VerticalsEnabled = cmd.Verticals
                .Select(v => Enum.TryParse<Vertical>(v, true, out var p) ? (Vertical?)p : null)
                .Where(v => v.HasValue).Select(v => v!.Value).ToList();
        }

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.ConfigChange,
            "Zone", cmd.ZoneId, reason: $"Zone updated — status now {zone.Status}",
            before: before, after: new { zone.Name, zone.Status }, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── PUT /admin/content/{key} ──────────────────────────────────────────────────
// Per-locale bodies (fr/en), draft/publish. Version history via UpdatedAt.

public record UpdateContentCommand(string Key, string Market,
    string? ContentFr, string? ContentEn,
    bool Publish, string StaffId, string StaffName) : IRequest<ZoneCommandResult>;

public class UpdateContentHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<UpdateContentCommand, ZoneCommandResult>
{
    public async Task<ZoneCommandResult> Handle(UpdateContentCommand cmd, CancellationToken ct)
    {
        async Task UpsertPage(string slug, string lang, string content)
        {
            var page = await db.AppPages.FirstOrDefaultAsync(
                p => p.Slug == slug && p.Market == cmd.Market && p.Language == lang, ct);

            if (page is null)
            {
                db.AppPages.Add(new AppPage
                {
                    Slug          = slug,
                    Market        = cmd.Market,
                    Language      = lang,
                    ContentFormat = "markdown",
                    Content       = content,
                    UpdatedAt     = DateTime.UtcNow
                });
            }
            else
            {
                page.Content   = content;
                page.UpdatedAt = DateTime.UtcNow;
            }
        }

        if (cmd.ContentFr is not null) await UpsertPage(cmd.Key, "fr", cmd.ContentFr);
        if (cmd.ContentEn is not null) await UpsertPage(cmd.Key, "en", cmd.ContentEn);

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.ConfigChange,
            "AppPage", cmd.Key, reason: $"Content {(cmd.Publish ? "published" : "saved as draft")}",
            after: new { Key = cmd.Key, Publish = cmd.Publish }, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── POST /admin/content/banners ───────────────────────────────────────────────
// Image URL, deep link, placement, schedule, ordering.

public record CreateBannerCommand(string ImageUrl, string? DeepLink, string Placement,
    DateTime? StartsAt, DateTime? EndsAt, int Order,
    string Market, string StaffId, string StaffName) : IRequest<ZoneCommandResult>;

public class CreateBannerHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<CreateBannerCommand, ZoneCommandResult>
{
    private static readonly HashSet<string> ValidPlacements =
        new(StringComparer.OrdinalIgnoreCase) { "home", "wallet" };

    public async Task<ZoneCommandResult> Handle(CreateBannerCommand cmd, CancellationToken ct)
    {
        if (!ValidPlacements.Contains(cmd.Placement))
            return new(false, "INVALID_PLACEMENT");

        var banner = new Banner
        {
            ImageUrl = cmd.ImageUrl,
            DeepLink = cmd.DeepLink,
            Placement = cmd.Placement.ToLower(),
            StartsAt  = cmd.StartsAt,
            EndsAt    = cmd.EndsAt,
            Order     = cmd.Order,
            Market    = cmd.Market,
            IsActive  = true
        };
        db.Banners.Add(banner);

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.ConfigChange,
            "Banner", banner.Id, reason: "Banner created",
            after: new { banner.Placement, banner.ImageUrl }, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null, new { id = banner.Id });
    }
}

// ── PUT /admin/config/versions ────────────────────────────────────────────────
// App version gates per platform: latest, minimum, gate (none|soft|force), store URL.

public record UpdateVersionConfigCommand(
    string Platform,   // ios | android
    string Latest, string Minimum, string Gate,
    string? StoreUrl,
    string Market, string StaffId, string StaffName) : IRequest<ZoneCommandResult>;

public class UpdateVersionConfigHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<UpdateVersionConfigCommand, ZoneCommandResult>
{
    private static readonly HashSet<string> ValidGates =
        new(StringComparer.OrdinalIgnoreCase) { "none", "soft", "force" };

    public async Task<ZoneCommandResult> Handle(UpdateVersionConfigCommand cmd, CancellationToken ct)
    {
        if (!ValidGates.Contains(cmd.Gate))
            return new(false, "INVALID_GATE");

        var config = await db.PlatformConfigs.FirstOrDefaultAsync(c => c.Market == cmd.Market, ct);
        if (config is null) return new(false, "CONFIG_NOT_FOUND");

        var before = new
        {
            config.MinAppVersionIos, config.LatestAppVersionIos,
            config.MinAppVersionAndroid, config.LatestAppVersionAndroid
        };

        if (cmd.Platform.Equals("ios", StringComparison.OrdinalIgnoreCase))
        {
            config.MinAppVersionIos    = cmd.Minimum;
            config.LatestAppVersionIos = cmd.Latest;
            if (cmd.StoreUrl is not null) config.IosStoreUrl = cmd.StoreUrl;
        }
        else if (cmd.Platform.Equals("android", StringComparison.OrdinalIgnoreCase))
        {
            config.MinAppVersionAndroid    = cmd.Minimum;
            config.LatestAppVersionAndroid = cmd.Latest;
            if (cmd.StoreUrl is not null) config.AndroidStoreUrl = cmd.StoreUrl;
        }
        else return new(false, "INVALID_PLATFORM");

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.ConfigChange,
            "PlatformConfig", config.Id,
            reason: $"App version gate updated — {cmd.Platform}: min={cmd.Minimum} latest={cmd.Latest} gate={cmd.Gate}",
            before: before, after: new { cmd.Platform, cmd.Minimum, cmd.Latest, cmd.Gate }, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}
