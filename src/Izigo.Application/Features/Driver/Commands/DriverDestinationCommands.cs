using Izigo.Application.Common.Helpers;
using Izigo.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Driver.Commands;

public record DriverDestinationDto(string? Label, double? Lat, double? Lng, DateTime? SetAt, int UsesLeftToday);

public record SetDestinationRequest(string Label, double Lat, double Lng);

// ── GET /driver/destination ───────────────────────────────────────────────────

public record GetDriverDestinationQuery(string DriverId) : IRequest<DriverDestinationDto>;

public class GetDriverDestinationHandler(IApplicationDbContext db)
    : IRequestHandler<GetDriverDestinationQuery, DriverDestinationDto>
{
    public async Task<DriverDestinationDto> Handle(GetDriverDestinationQuery req, CancellationToken ct)
    {
        var dp = await db.DriverProfiles.FirstOrDefaultAsync(d => d.UserId == req.DriverId, ct)
                 ?? throw new KeyNotFoundException("Driver profile not found.");
        return DriverDestinationMapper.ToDto(dp);
    }
}

// ── PUT /driver/destination ───────────────────────────────────────────────────

public record SetDriverDestinationCommand(string DriverId, SetDestinationRequest Request) : IRequest<DriverDestinationDto>;

public class SetDriverDestinationHandler(IApplicationDbContext db)
    : IRequestHandler<SetDriverDestinationCommand, DriverDestinationDto>
{
    public async Task<DriverDestinationDto> Handle(SetDriverDestinationCommand cmd, CancellationToken ct)
    {
        var req = cmd.Request;
        if (req.Lat is < -90 or > 90 || req.Lng is < -180 or > 180 || string.IsNullOrWhiteSpace(req.Label))
            throw new ArgumentException("VALIDATION_ERROR: Pick a destination on the map.");

        var dp = await db.DriverProfiles.FirstOrDefaultAsync(d => d.UserId == cmd.DriverId, ct)
                 ?? throw new KeyNotFoundException("Driver profile not found.");

        var today = DateTime.UtcNow.Date;
        if (dp.DestinationUsesOn != today)
        {
            dp.DestinationUsesOn = today;
            dp.DestinationUsesToday = 0;
        }
        // Changing an active destination does not cost another use.
        if (dp.DestinationLat is null)
        {
            if (dp.DestinationUsesToday >= DriverDestination.MaxUsesPerDay)
                throw new InvalidOperationException(
                    $"CONFLICT: You can set a destination {DriverDestination.MaxUsesPerDay} times a day. Try again tomorrow.");
            dp.DestinationUsesToday++;
        }

        dp.DestinationLat = (decimal)req.Lat;
        dp.DestinationLng = (decimal)req.Lng;
        dp.DestinationLabel = req.Label.Trim();
        dp.DestinationSetAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return DriverDestinationMapper.ToDto(dp);
    }
}

// ── DELETE /driver/destination ────────────────────────────────────────────────

public record ClearDriverDestinationCommand(string DriverId) : IRequest<DriverDestinationDto>;

public class ClearDriverDestinationHandler(IApplicationDbContext db)
    : IRequestHandler<ClearDriverDestinationCommand, DriverDestinationDto>
{
    public async Task<DriverDestinationDto> Handle(ClearDriverDestinationCommand cmd, CancellationToken ct)
    {
        var dp = await db.DriverProfiles.FirstOrDefaultAsync(d => d.UserId == cmd.DriverId, ct)
                 ?? throw new KeyNotFoundException("Driver profile not found.");
        DriverDestinationMapper.Clear(dp);
        await db.SaveChangesAsync(ct);
        return DriverDestinationMapper.ToDto(dp);
    }
}

public static class DriverDestinationMapper
{
    public static DriverDestinationDto ToDto(Domain.Entities.DriverProfile dp)
    {
        var usedToday = dp.DestinationUsesOn == DateTime.UtcNow.Date ? dp.DestinationUsesToday : 0;
        return new DriverDestinationDto(
            dp.DestinationLabel,
            (double?)dp.DestinationLat,
            (double?)dp.DestinationLng,
            dp.DestinationSetAt,
            Math.Max(0, DriverDestination.MaxUsesPerDay - usedToday));
    }

    public static void Clear(Domain.Entities.DriverProfile dp)
    {
        dp.DestinationLat = null;
        dp.DestinationLng = null;
        dp.DestinationLabel = null;
        dp.DestinationSetAt = null;
    }
}
