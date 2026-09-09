using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Places.Dtos;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Places.Queries;

// ── GET /places ───────────────────────────────────────────────────────────────

public record GetPlacesQuery(string UserId) : IRequest<List<PlaceDto>>;

public class GetPlacesHandler(IApplicationDbContext db)
    : IRequestHandler<GetPlacesQuery, List<PlaceDto>>
{
    public async Task<List<PlaceDto>> Handle(GetPlacesQuery req, CancellationToken ct)
        => await db.SavedPlaces
            .Where(p => p.UserId == req.UserId)
            .OrderByDescending(p => p.IsDefault)
            .ThenBy(p => p.Label)
            .Select(p => Map(p))
            .ToListAsync(ct);

    internal static PlaceDto Map(Domain.Entities.SavedPlace p) => new(
        p.Id, p.Label, p.Name, p.Address,
        (double)p.Lat, (double)p.Lng,
        p.PlaceId, p.Building, p.Floor,
        p.Instructions, p.ContactPhone, p.IsDefault);
}

// ── GET /places/recent — server-derived from trip history ─────────────────────

public record GetRecentPlacesQuery(string UserId, int Limit) : IRequest<List<PlaceDto>>;

public class GetRecentPlacesHandler(IApplicationDbContext db)
    : IRequestHandler<GetRecentPlacesQuery, List<PlaceDto>>
{
    public async Task<List<PlaceDto>> Handle(GetRecentPlacesQuery req, CancellationToken ct)
    {
        // Derive recent destinations from completed trips
        var recents = await db.Trips
            .Where(t => t.RiderId == req.UserId && t.JobState == JobState.Completed)
            .OrderByDescending(t => t.CompletedAt)
            .Select(t => new
            {
                t.DropoffLabel,
                t.DropoffLat,
                t.DropoffLng,
                t.DropoffPlaceId
            })
            .Take(req.Limit * 3)   // over-fetch to deduplicate
            .ToListAsync(ct);

        // Deduplicate by dropoff label
        return recents
            .DistinctBy(r => r.DropoffLabel)
            .Take(req.Limit)
            .Select(r => new PlaceDto(
                Id: $"recent_{Guid.CreateVersion7():N}",   // synthetic, not stored
                Label: "custom",
                Name: r.DropoffLabel,
                Address: r.DropoffLabel,
                Lat: (double)r.DropoffLat,
                Lng: (double)r.DropoffLng,
                PlaceId: r.DropoffPlaceId,
                Building: null, Floor: null, Instructions: null,
                ContactPhone: null, IsDefault: false))
            .ToList();
    }
}
