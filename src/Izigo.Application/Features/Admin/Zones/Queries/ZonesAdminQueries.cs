using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Models;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Admin.Zones.Queries;

// ── A18: ZONES & CONTENT ─────────────────────────────────────────────────────

// ── GET /admin/zones ──────────────────────────────────────────────────────────
// Status, verticals enabled, drivers online, trips over period.

public record ZoneRowDto(string Id, string Name, string Market, string Status,
    IEnumerable<string> VerticalsEnabled, int DriversOnline, int TripsLast24h,
    decimal CenterLat, decimal CenterLng);

public record GetAdminZonesQuery(string Market) : IRequest<object>;

public class GetAdminZonesHandler(IApplicationDbContext db)
    : IRequestHandler<GetAdminZonesQuery, object>
{
    public async Task<object> Handle(GetAdminZonesQuery req, CancellationToken ct)
    {
        var zones = await db.Zones
            .Where(z => z.Market == req.Market)
            .OrderBy(z => z.Name)
            .ToListAsync(ct);

        var cutoff = DateTime.UtcNow.AddHours(-24);

        var tripCounts = await db.Trips
            .Where(t => t.Market == req.Market && t.CreatedAt >= cutoff)
            .GroupBy(t => t.PickupLabel)   // no ZoneId on Trip — approximate by count
            .Select(g => new { Label = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var driversOnline = await db.DriverProfiles
            .CountAsync(d => d.IsOnline, ct);

        var rows = zones.Select(z => new ZoneRowDto(
            z.Id, z.Name, z.Market, z.Status,
            z.VerticalsEnabled.Select(v => v.ToString().ToLower()),
            driversOnline,
            tripCounts.Sum(t => t.Count),   // global approximation — zone-level needs spatial
            z.CenterLat, z.CenterLng)).ToArray();

        return new { success = true, data = rows };
    }
}

// ── GET /admin/content ────────────────────────────────────────────────────────
// Legal pages, FAQs, onboarding slides, banners, reason lists — each with
// the app endpoint it serves and updated_at.

public record GetAdminContentQuery(string Market) : IRequest<object>;

public class GetAdminContentHandler(IApplicationDbContext db)
    : IRequestHandler<GetAdminContentQuery, object>
{
    public async Task<object> Handle(GetAdminContentQuery req, CancellationToken ct)
    {
        var pages = await db.AppPages
            .Where(p => p.Market == req.Market)
            .Select(p => new { type = "page", key = p.Slug, p.Language, p.ContentFormat,
                               snippet = p.Content.Length > 200 ? p.Content.Substring(0, 200) + "…" : p.Content,
                               app_endpoint = $"/pages/{p.Slug}", updated_at = p.UpdatedAt })
            .ToListAsync(ct);

        var slides = await db.OnboardingSlides
            .Where(s => s.Market == req.Market)
            .OrderBy(s => s.Order)
            .Select(s => new { type = "slide", key = $"slide_{s.Id}", s.Audience,
                               s.Title, s.Subtitle, s.ImageUrl, s.Order, s.IsActive,
                               app_endpoint = "/onboarding/slides", updated_at = s.CreatedAt })
            .ToListAsync(ct);

        var banners = await db.Banners
            .Where(b => b.Market == req.Market)
            .OrderBy(b => b.Order)
            .Select(b => new { type = "banner", key = $"banner_{b.Id}", b.Placement,
                               b.ImageUrl, b.DeepLink, b.IsActive, b.StartsAt, b.EndsAt, b.Order,
                               app_endpoint = "/banners", updated_at = b.CreatedAt })
            .ToListAsync(ct);

        var reasons = await db.CancellationReasons
            .Where(r => r.IsActive)
            .Select(r => new { type = "reason", key = r.Code, r.Label, r.Language,
                               r.Audience, app_endpoint = "/cancellation-reasons",
                               updated_at = r.CreatedAt })
            .ToListAsync(ct);

        return new
        {
            success = true,
            data = new
            {
                pages   = pages.Cast<object>().ToList(),
                slides  = slides.Cast<object>().ToList(),
                banners = banners.Cast<object>().ToList(),
                reasons = reasons.Cast<object>().ToList()
            }
        };
    }
}
