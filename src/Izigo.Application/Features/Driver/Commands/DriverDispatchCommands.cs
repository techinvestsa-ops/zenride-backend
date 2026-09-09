using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Driver.Dtos;
using Izigo.Application.Features.Realtime.Dtos;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Driver.Commands;

// ── POST /driver/status — go online / offline ─────────────────────────────────

public record SetDriverStatusCommand(string DriverId, SetStatusRequest Request)
    : IRequest<DriverStatusDto>;

public class SetDriverStatusHandler(IApplicationDbContext db)
    : IRequestHandler<SetDriverStatusCommand, DriverStatusDto>
{
    public async Task<DriverStatusDto> Handle(SetDriverStatusCommand cmd, CancellationToken ct)
    {
        var dp = await db.DriverProfiles
            .Include(d => d.DriverWallet)
            .FirstOrDefaultAsync(d => d.UserId == cmd.DriverId, ct)
            ?? throw new KeyNotFoundException("Driver profile not found.");

        if (cmd.Request.Online)
        {
            if (dp.KycStatus != KycStatus.Approved)
                throw new InvalidOperationException("FORBIDDEN: KYC must be approved before going online.");

            if (!dp.OnboardingComplete)
                throw new InvalidOperationException("FORBIDDEN: Onboarding is not complete.");

            const long CashCap = 10_000;
            var cashOwed = dp.DriverWallet?.PendingCashSettlement ?? 0;
            if (cashOwed >= CashCap)
                throw new InvalidOperationException(
                    $"FORBIDDEN: Cash settlement balance ({cashOwed}) exceeds the cap. Please settle before going online.");
        }

        dp.IsOnline = cmd.Request.Online;
        await db.SaveChangesAsync(ct);

        var cashSettlement = dp.DriverWallet?.PendingCashSettlement ?? 0;
        const long Cap = 10_000;
        return new DriverStatusDto(
            IsOnline: dp.IsOnline,
            KycStatus: dp.KycStatus.ToString().ToLower(),
            OnboardingComplete: dp.OnboardingComplete,
            VerticalsAllowed: dp.VerticalsAllowed.Select(v => v.ToString().ToLower()).ToArray(),
            PendingCashSettlement: cashSettlement,
            CashCapBlocked: cashSettlement >= Cap);
    }
}

// ── POST /driver/location — GPS heartbeat ─────────────────────────────────────

public record UpdateLocationCommand(string DriverId, UpdateLocationRequest Request) : IRequest;

public class UpdateLocationHandler(IApplicationDbContext db, IRealtimeService realtime)
    : IRequestHandler<UpdateLocationCommand>
{
    public async Task Handle(UpdateLocationCommand cmd, CancellationToken ct)
    {
        var req = cmd.Request;

        var dp = await db.DriverProfiles
            .FirstOrDefaultAsync(d => d.UserId == cmd.DriverId, ct)
            ?? throw new KeyNotFoundException("Driver profile not found.");

        dp.LastLat        = (decimal)req.Lat;
        dp.LastLng        = (decimal)req.Lng;
        dp.LastHeading    = req.Heading.HasValue ? (decimal)req.Heading.Value : dp.LastHeading;
        dp.LastLocationAt = DateTime.UtcNow;

        // Write location history point
        db.DriverLocationPoints.Add(new DriverLocationPoint
        {
            DriverId   = cmd.DriverId,
            Lat        = (decimal)req.Lat,
            Lng        = (decimal)req.Lng,
            Heading    = req.Heading.HasValue ? (decimal)req.Heading.Value : null,
            Speed      = req.Speed.HasValue ? (decimal)req.Speed.Value : null,
            Accuracy   = req.Accuracy.HasValue ? (decimal)req.Accuracy.Value : null,
            RecordedAt = DateTime.UtcNow,
            JobId      = req.JobId,
        });

        await db.SaveChangesAsync(ct);

        // driver.location → presence-trip.{trip_id}  (spec: throttle ~3–5 s on-trip)
        // The active job_id is passed in the batch so we know which trip room to notify.
        if (!string.IsNullOrEmpty(req.JobId))
            await realtime.PublishToTripAsync(req.JobId, "driver.location",
                new DriverLocationEvent(req.JobId, req.Lat, req.Lng, req.Heading, EtaMin: null), ct);
    }
}
