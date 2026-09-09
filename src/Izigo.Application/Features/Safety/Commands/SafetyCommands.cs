using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Settings;
using Izigo.Application.Features.Safety.Dtos;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Izigo.Application.Features.Safety.Commands;

// ── POST /safety/sos ──────────────────────────────────────────────────────────

public record TriggerSosCommand(string UserId, TriggerSosRequest Request)
    : IRequest<SosDto>;

public class TriggerSosHandler(IApplicationDbContext db, IRealtimeService realtime, IPushService push)
    : IRequestHandler<TriggerSosCommand, SosDto>
{
    private const string EmergencyNumber = "+225 1717";

    public async Task<SosDto> Handle(TriggerSosCommand cmd, CancellationToken ct)
    {
        var req = cmd.Request;

        if (!Enum.TryParse<SosType>(req.Type, true, out var sosType))
            throw new ArgumentException("VALIDATION_ERROR: Invalid SOS type.");

        // Derive market from the linked trip, or fall back to "ci"
        var market = "ci";
        if (req.TripId is not null)
        {
            var tripMarket = await db.Trips
                .Where(t => t.Id == req.TripId)
                .Select(t => t.Market)
                .FirstOrDefaultAsync(ct);
            if (tripMarket is not null) market = tripMarket;
        }

        var incident = new SosIncident
        {
            UserId = cmd.UserId,
            Source = req.Source,
            Type   = sosType,
            Status = SosStatus.Active,
            Lat    = (decimal)req.Lat,
            Lng    = (decimal)req.Lng,
            TripId = req.TripId,
            Notes  = req.Note,
            Market = market
        };

        db.SosIncidents.Add(incident);
        await db.SaveChangesAsync(ct);

        // Push sos.raised to admin realtime channel — must arrive within 1 second
        await realtime.PublishToAdminAsync(market, "sos.raised", new
        {
            incident_id = incident.Id,
            type        = incident.Type.ToString().ToLower(),
            source      = incident.Source,
            lat         = incident.Lat,
            lng         = incident.Lng,
            trip_id     = incident.TripId,
            raised_at   = incident.CreatedAt
        }, ct);

        // Notify emergency contacts via push notification
        var contacts = await db.EmergencyContacts
            .Where(c => c.UserId == cmd.UserId)
            .Select(c => c.UserId)
            .ToListAsync(ct);

        if (contacts.Count > 0)
        {
            var tokens = await db.UserDevices
                .Where(d => contacts.Contains(d.UserId) && d.FcmToken != null)
                .Select(d => d.FcmToken!)
                .Distinct().ToListAsync(ct);

            if (tokens.Count > 0)
                await push.SendBatchAsync(tokens,
                    "Emergency SOS Alert",
                    "A contact has triggered an SOS emergency alert.",
                    "sos_alert", ct: ct);
        }

        return new SosDto(incident.Id, incident.Status.ToString().ToLower(),
            incident.Source, (double)incident.Lat, (double)incident.Lng,
            incident.TripId, EmergencyNumber, incident.CreatedAt);
    }
}

// ── POST /safety/sos/{id}/resolve ─────────────────────────────────────────────

public record ResolveSosCommand(string UserId, string SosId) : IRequest<SosDto>;

public class ResolveSosHandler(IApplicationDbContext db)
    : IRequestHandler<ResolveSosCommand, SosDto>
{
    private const string EmergencyNumber = "+225 1717";

    public async Task<SosDto> Handle(ResolveSosCommand cmd, CancellationToken ct)
    {
        var incident = await db.SosIncidents
            .FirstOrDefaultAsync(s => s.Id == cmd.SosId && s.UserId == cmd.UserId, ct)
            ?? throw new KeyNotFoundException("SOS incident not found.");

        if (incident.Status != SosStatus.Active)
            throw new InvalidOperationException("CONFLICT: SOS is not active.");

        incident.Status  = SosStatus.Resolved;
        incident.Outcome = "resolved";
        await db.SaveChangesAsync(ct);

        return new SosDto(incident.Id, incident.Status.ToString().ToLower(),
            incident.Source, (double)incident.Lat, (double)incident.Lng,
            incident.TripId, EmergencyNumber, incident.CreatedAt);
    }
}

// ── POST /trips/{id}/share — generate share token ────────────────────────────

public record ShareTripCommand(string RiderId, string TripId) : IRequest<ShareTokenDto>;

public class ShareTripHandler(IApplicationDbContext db, IOptions<AppSettings> appOptions)
    : IRequestHandler<ShareTripCommand, ShareTokenDto>
{
    public async Task<ShareTokenDto> Handle(ShareTripCommand cmd, CancellationToken ct)
    {
        var cfg      = appOptions.Value;
        var shareUrl = cfg.ShareBaseUrl.TrimEnd('/');

        var trip = await db.Trips
            .FirstOrDefaultAsync(t => t.Id == cmd.TripId && t.RiderId == cmd.RiderId, ct)
            ?? throw new KeyNotFoundException("Trip not found.");

        if (trip.ShareToken != null && trip.ShareTokenExpiresAt > DateTime.UtcNow)
            return new ShareTokenDto(
                $"{shareUrl}/{trip.ShareToken}",
                trip.ShareTokenExpiresAt!.Value);

        var token     = Guid.CreateVersion7().ToString("N")[..16];
        var expiresAt = DateTime.UtcNow.AddHours(cfg.ShareTokenExpiryHours);

        trip.ShareToken          = token;
        trip.ShareTokenExpiresAt = expiresAt;
        await db.SaveChangesAsync(ct);

        return new ShareTokenDto($"{shareUrl}/{token}", expiresAt);
    }
}

// ── POST /trips/{id}/report ───────────────────────────────────────────────────

public record ReportTripCommand(string UserId, string UserRole, string TripId, ReportTripRequest Request)
    : IRequest;

public class ReportTripHandler(IApplicationDbContext db)
    : IRequestHandler<ReportTripCommand>
{
    public async Task Handle(ReportTripCommand cmd, CancellationToken ct)
    {
        _ = await db.Trips.FirstOrDefaultAsync(t => t.Id == cmd.TripId, ct)
            ?? throw new KeyNotFoundException("Trip not found.");

        db.SupportTickets.Add(new SupportTicket
        {
            UserId      = cmd.UserId,
            UserRole    = cmd.UserRole,
            Category    = cmd.Request.Category,
            Description = cmd.Request.Description,
            TripId      = cmd.TripId,
            Reference   = $"TKT-{Guid.CreateVersion7():N}"[..12],
            Market      = "ci",
        });

        await db.SaveChangesAsync(ct);
    }
}

// ── POST /safety/checkin ──────────────────────────────────────────────────────

public record SafetyCheckinCommand(string UserId, SafetyCheckinRequest? Request) : IRequest;

public class SafetyCheckinHandler(IApplicationDbContext db)
    : IRequestHandler<SafetyCheckinCommand>
{
    public async Task Handle(SafetyCheckinCommand cmd, CancellationToken ct)
    {
        // Record a system notification confirming the check-in
        db.Notifications.Add(new Notification
        {
            UserId   = cmd.UserId,
            Type     = "safety_checkin",
            Title    = "Safety check-in recorded",
            Body     = cmd.Request?.Note ?? "You're marked as safe.",
            Group    = NotificationGroup.System,
        });

        await db.SaveChangesAsync(ct);
    }
}
