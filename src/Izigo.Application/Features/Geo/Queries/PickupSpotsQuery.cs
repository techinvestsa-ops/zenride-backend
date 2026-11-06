using Izigo.Application.Common.Helpers;
using Izigo.Application.Common.Interfaces;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Geo.Queries;

public record PickupSpotDto(string Label, double Lat, double Lng, int DistanceM, int Pickups);

/// <summary>
/// Suggested pickup spots near the rider: places where completed rides were
/// actually picked up in the last 90 days, so drivers know them and can
/// stop there. Pickups are grouped on a ~30 m grid; a spot needs at least
/// <see cref="MinPickups"/> of them. Returns up to 3, busiest first.
/// </summary>
public record GetPickupSpotsQuery(double Lat, double Lng) : IRequest<PickupSpotDto[]>;

public class GetPickupSpotsHandler(IApplicationDbContext db) : IRequestHandler<GetPickupSpotsQuery, PickupSpotDto[]>
{
    private const double RadiusM = 250;
    private const decimal BoxDeg = 0.0025m;   // ~275 m, pre-filter for RadiusM
    private const double GridDeg = 0.0003;    // ~33 m cells
    public const int MinPickups = 3;

    public async Task<PickupSpotDto[]> Handle(GetPickupSpotsQuery req, CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddDays(-90);
        var (la, lo) = ((decimal)req.Lat, (decimal)req.Lng);

        var pickups = await db.Trips
            .Where(t => t.JobState == JobState.Completed && t.Vertical == Vertical.Ride &&
                        t.CreatedAt >= since &&
                        t.PickupLat >= la - BoxDeg && t.PickupLat <= la + BoxDeg &&
                        t.PickupLng >= lo - BoxDeg && t.PickupLng <= lo + BoxDeg)
            .Select(t => new { t.PickupLat, t.PickupLng, t.PickupLabel })
            .Take(2_000)
            .ToListAsync(ct);

        return pickups
            .Select(p => new { Lat = (double)p.PickupLat, Lng = (double)p.PickupLng, p.PickupLabel })
            .Where(p => DriverDestination.Metres(req.Lat, req.Lng, p.Lat, p.Lng) <= RadiusM)
            .GroupBy(p => (Math.Round(p.Lat / GridDeg), Math.Round(p.Lng / GridDeg)))
            .Where(g => g.Count() >= MinPickups)
            .Select(g =>
            {
                var lat = g.Average(p => p.Lat);
                var lng = g.Average(p => p.Lng);
                var label = g.Where(p => !string.IsNullOrWhiteSpace(p.PickupLabel))
                             .GroupBy(p => p.PickupLabel)
                             .OrderByDescending(l => l.Count())
                             .Select(l => l.Key)
                             .FirstOrDefault() ?? "Popular pickup spot";
                return new PickupSpotDto(label, Math.Round(lat, 6), Math.Round(lng, 6),
                    (int)DriverDestination.Metres(req.Lat, req.Lng, lat, lng), g.Count());
            })
            .OrderByDescending(s => s.Pickups)
            .ThenBy(s => s.DistanceM)
            .Take(3)
            .ToArray();
    }
}
