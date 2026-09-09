using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Models;
using Izigo.Application.Common.Settings;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Izigo.Application.Features.Admin.Safety.Queries;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record SosIncidentRowDto(
    string Id, string Type, string Source, string Status,
    string? TripId, string UserId, string UserName,
    decimal Lat, decimal Lng, string Market, DateTime RaisedAt,
    string? AcknowledgedByStaffId, DateTime? AcknowledgedAt,
    double? ResponseSeconds, string? Outcome, string? AuthorityName);

public record LocationPointDto(decimal Lat, decimal Lng, DateTime RecordedAt);

public record EmergencyContactDto(string Name, string Phone, string? Relationship);

public record SosIncidentDetailDto(
    SosIncidentRowDto Incident,
    IEnumerable<LocationPointDto> LocationTrail,
    IEnumerable<EmergencyContactDto> EmergencyContacts);

// ── GET /admin/safety/incidents ───────────────────────────────────────────────

public record GetSosIncidentsQuery(string Market, string? Status, string? Type,
    string? Source, DateTime? From, DateTime? To, int Page, int PerPage) : IRequest<object>;

public class GetSosIncidentsHandler(IApplicationDbContext db)
    : IRequestHandler<GetSosIncidentsQuery, object>
{
    public async Task<object> Handle(GetSosIncidentsQuery req, CancellationToken ct)
    {
        var query = db.SosIncidents.Where(s => s.Market == req.Market).AsQueryable();

        if (!string.IsNullOrWhiteSpace(req.Status) &&
            Enum.TryParse<SosStatus>(req.Status, true, out var parsedStatus))
            query = query.Where(s => s.Status == parsedStatus);

        if (!string.IsNullOrWhiteSpace(req.Type) &&
            Enum.TryParse<SosType>(req.Type, true, out var parsedType))
            query = query.Where(s => s.Type == parsedType);

        if (!string.IsNullOrWhiteSpace(req.Source))
            query = query.Where(s => s.Source == req.Source.ToLower());

        if (req.From.HasValue) query = query.Where(s => s.CreatedAt >= req.From.Value);
        if (req.To.HasValue)   query = query.Where(s => s.CreatedAt <= req.To.Value);

        var perPage  = Math.Max(1, Math.Min(100, req.PerPage));
        var total    = await query.CountAsync(ct);
        var lastPage = (int)Math.Ceiling((double)total / perPage);

        var incidents = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip((req.Page - 1) * perPage).Take(perPage)
            .Select(s => new
            {
                s.Id, s.Type, s.Source, s.Status, s.TripId, s.UserId,
                s.Lat, s.Lng, s.Market, s.CreatedAt,
                s.AcknowledgedByStaffId, s.AcknowledgedAt,
                s.Outcome, s.AuthorityName
            })
            .ToListAsync(ct);

        var userIds = incidents.Select(s => s.UserId).Distinct().ToList();
        var users   = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName })
            .ToDictionaryAsync(u => u.Id, ct);

        var rows = incidents.Select(s =>
        {
            users.TryGetValue(s.UserId, out var user);
            var responseSeconds = s.AcknowledgedAt.HasValue
                ? (double?)(s.AcknowledgedAt.Value - s.CreatedAt).TotalSeconds
                : null;
            return new SosIncidentRowDto(
                s.Id, s.Type.ToString().ToLower(), s.Source,
                s.Status.ToString().ToLower(), s.TripId, s.UserId,
                user is not null ? $"{user.FirstName} {user.LastName}".Trim() : "—",
                s.Lat, s.Lng, s.Market, s.CreatedAt,
                s.AcknowledgedByStaffId, s.AcknowledgedAt,
                responseSeconds, s.Outcome, s.AuthorityName);
        }).ToArray();

        var facets = new Dictionary<string, Dictionary<string, int>>
        {
            ["status"] = incidents.GroupBy(s => s.Status.ToString().ToLower())
                             .ToDictionary(g => g.Key, g => g.Count()),
            ["type"]   = incidents.GroupBy(s => s.Type.ToString().ToLower())
                             .ToDictionary(g => g.Key, g => g.Count()),
            ["source"] = incidents.GroupBy(s => s.Source)
                             .ToDictionary(g => g.Key, g => g.Count())
        };

        return AdminApiResponse.Ok(rows, new AdminPagedMeta(req.Page, perPage, total, lastPage, facets));
    }
}

// ── GET /admin/safety/incidents/{id} ─────────────────────────────────────────

public record GetSosIncidentDetailQuery(string IncidentId) : IRequest<SosIncidentDetailDto?>;

public class GetSosIncidentDetailHandler(IApplicationDbContext db, IOptions<OpsSettings> opsOptions)
    : IRequestHandler<GetSosIncidentDetailQuery, SosIncidentDetailDto?>
{
    public async Task<SosIncidentDetailDto?> Handle(GetSosIncidentDetailQuery req, CancellationToken ct)
    {
        var incident = await db.SosIncidents
            .Where(s => s.Id == req.IncidentId)
            .Select(s => new
            {
                s.Id, s.Type, s.Source, s.Status, s.TripId, s.UserId,
                s.Lat, s.Lng, s.Market, s.CreatedAt,
                s.AcknowledgedByStaffId, s.AcknowledgedAt,
                s.Outcome, s.Notes, s.AuthorityName, s.AuthorityReference
            })
            .FirstOrDefaultAsync(ct);

        if (incident is null) return null;

        var user = await db.Users
            .Where(u => u.Id == incident.UserId)
            .Select(u => new { u.FirstName, u.LastName })
            .FirstOrDefaultAsync(ct);

        var responseSeconds = incident.AcknowledgedAt.HasValue
            ? (double?)(incident.AcknowledgedAt.Value - incident.CreatedAt).TotalSeconds
            : null;

        var row = new SosIncidentRowDto(
            incident.Id, incident.Type.ToString().ToLower(), incident.Source,
            incident.Status.ToString().ToLower(), incident.TripId, incident.UserId,
            user is not null ? $"{user.FirstName} {user.LastName}".Trim() : "—",
            incident.Lat, incident.Lng, incident.Market, incident.CreatedAt,
            incident.AcknowledgedByStaffId, incident.AcknowledgedAt,
            responseSeconds, incident.Outcome, incident.AuthorityName);

        // Driver GPS trail around the SOS — 5 min before to 30 min after
        IEnumerable<LocationPointDto> trail = [];
        if (incident.TripId is not null)
        {
            var driverId = await db.Trips
                .Where(t => t.Id == incident.TripId)
                .Select(t => t.DriverId)
                .FirstOrDefaultAsync(ct);

            if (driverId is not null)
                trail = await db.DriverLocationPoints
                    .Where(p => p.DriverId == driverId &&
                                p.RecordedAt >= incident.CreatedAt.AddMinutes(-opsOptions.Value.SosTrailBeforeMinutes) &&
                                p.RecordedAt <= incident.CreatedAt.AddMinutes(opsOptions.Value.SosTrailAfterMinutes))
                    .OrderBy(p => p.RecordedAt)
                    .Select(p => new LocationPointDto(p.Lat, p.Lng, p.RecordedAt))
                    .ToListAsync(ct);
        }

        var contacts = await db.EmergencyContacts
            .Where(c => c.UserId == incident.UserId)
            .Select(c => new EmergencyContactDto(c.Name, c.Phone, c.Relationship))
            .ToListAsync(ct);

        return new SosIncidentDetailDto(row, trail, contacts);
    }
}

// ── GET /admin/safety/metrics ─────────────────────────────────────────────────

public record GetSafetyMetricsQuery(string Market) : IRequest<object>;

public class GetSafetyMetricsHandler(IApplicationDbContext db)
    : IRequestHandler<GetSafetyMetricsQuery, object>
{
    public async Task<object> Handle(GetSafetyMetricsQuery req, CancellationToken ct)
    {
        var incidents = await db.SosIncidents
            .Where(s => s.Market == req.Market)
            .Select(s => new { s.CreatedAt, s.AcknowledgedAt, s.Type })
            .ToListAsync(ct);

        var responseTimes = incidents
            .Where(s => s.AcknowledgedAt.HasValue)
            .Select(s => (s.AcknowledgedAt!.Value - s.CreatedAt).TotalSeconds)
            .OrderBy(x => x)
            .ToList();

        var median = responseTimes.Count > 0 ? responseTimes[responseTimes.Count / 2] : 0d;
        var p95    = responseTimes.Count > 0
            ? responseTimes[Math.Max(0, (int)(responseTimes.Count * 0.95) - 1)]
            : 0d;

        var totalTrips    = await db.Trips.CountAsync(t => t.Market == req.Market, ct);
        var incidentsPer1k = totalTrips > 0
            ? Math.Round((double)incidents.Count / totalTrips * 1000, 2)
            : 0d;

        var byType = incidents
            .GroupBy(s => s.Type.ToString().ToLower())
            .Select(g => (object)new { type = g.Key, count = g.Count() })
            .ToList();

        return new
        {
            success = true,
            data = new
            {
                median_response_seconds  = Math.Round(median, 1),
                p95_response_seconds     = Math.Round(p95, 1),
                incidents_per_1000_trips = incidentsPer1k,
                by_type                  = byType,
                by_zone                  = Array.Empty<object>()
            }
        };
    }
}
