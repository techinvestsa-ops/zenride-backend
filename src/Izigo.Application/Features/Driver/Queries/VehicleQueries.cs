using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Models;
using Izigo.Application.Features.Driver.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Driver.Queries;

// ── GET /driver/vehicles ──────────────────────────────────────────────────────

public record GetVehiclesQuery(string DriverId) : IRequest<List<VehicleDto>>;

public class GetVehiclesHandler(IApplicationDbContext db)
    : IRequestHandler<GetVehiclesQuery, List<VehicleDto>>
{
    public async Task<List<VehicleDto>> Handle(GetVehiclesQuery req, CancellationToken ct)
        => await db.Vehicles
            .Where(v => v.DriverId == req.DriverId)
            .OrderByDescending(v => v.IsActive)
            .Select(v => new VehicleDto(
                v.Id, v.Make, v.Model, v.Year, v.Color, v.Plate,
                v.Type.ToString().ToLower(), v.Seats, v.IsActive, v.PendingReview))
            .ToListAsync(ct);
}

// ── GET /driver/documents ─────────────────────────────────────────────────────

public record GetDocumentsQuery(string DriverId) : IRequest<List<DriverDocumentDto>>;

public class GetDocumentsHandler(IApplicationDbContext db)
    : IRequestHandler<GetDocumentsQuery, List<DriverDocumentDto>>
{
    public async Task<List<DriverDocumentDto>> Handle(GetDocumentsQuery req, CancellationToken ct)
    {
        var now  = DateTime.UtcNow.Date;
        var docs = await db.DriverDocuments
            .Where(d => d.DriverId == req.DriverId)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);

        return docs.Select(d => new DriverDocumentDto(
            d.Id,
            d.Type.ToString().ToLower(),
            d.FileUrl,
            d.BackFileUrl,
            d.Status.ToString().ToLower(),
            d.RejectionReason,
            d.ExpiresAt,
            DaysToExpiry: d.ExpiresAt.HasValue ? (int)(d.ExpiresAt.Value.Date - now).TotalDays : null,
            d.CreatedAt)).ToList();
    }
}

// ── GET /driver/preferences ───────────────────────────────────────────────────

public record GetDriverPreferencesQuery(string DriverId) : IRequest<DriverPreferencesDto>;

public class GetDriverPreferencesHandler(IApplicationDbContext db)
    : IRequestHandler<GetDriverPreferencesQuery, DriverPreferencesDto>
{
    public async Task<DriverPreferencesDto> Handle(GetDriverPreferencesQuery req, CancellationToken ct)
    {
        var prefs = await db.UserPreferences
            .Where(p => p.UserId == req.DriverId && p.Key.StartsWith("driver:"))
            .ToDictionaryAsync(p => p.Key, p => p.Value, ct);

        var verticalStr = prefs.GetValueOrDefault("driver:verticals", "ride,coride,package");
        var verticals   = verticalStr.Split(',', StringSplitOptions.RemoveEmptyEntries);

        double? maxPickup = prefs.TryGetValue("driver:max_pickup_km", out var mpk)
            ? double.TryParse(mpk, out var d) ? d : null : null;

        return new DriverPreferencesDto(
            Verticals:            verticals,
            MaxPickupDistanceKm:  maxPickup,
            AutoAccept:           GetBool(prefs, "driver:auto_accept", false),
            AcceptCash:           GetBool(prefs, "driver:accept_cash", true),
            AcceptWallet:         GetBool(prefs, "driver:accept_wallet", true),
            AcceptCard:           GetBool(prefs, "driver:accept_card", true),
            AcceptMobileMoney:    GetBool(prefs, "driver:accept_mobile_money", true),
            NotifyOnNewOffer:     GetBool(prefs, "driver:notify_on_offer", true),
            SilentMode:           GetBool(prefs, "driver:silent_mode", false));
    }

    private static bool GetBool(Dictionary<string, string> d, string key, bool def)
        => d.TryGetValue(key, out var v) ? v == "true" : def;
}
