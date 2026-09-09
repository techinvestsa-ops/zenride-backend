using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Auth.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Auth.Queries;

public record GetSessionsQuery(string UserId, string? CurrentDeviceId) : IRequest<List<SessionDto>>;

public class GetSessionsHandler(IApplicationDbContext db)
    : IRequestHandler<GetSessionsQuery, List<SessionDto>>
{
    public async Task<List<SessionDto>> Handle(GetSessionsQuery req, CancellationToken ct)
    {
        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == req.UserId && !t.IsRevoked && t.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

        var deviceIds = tokens
            .Select(t => t.DeviceId)
            .Where(d => d != null)
            .Distinct()
            .ToList();

        var devices = await db.UserDevices
            .Where(d => d.UserId == req.UserId && deviceIds.Contains(d.DeviceId))
            .ToDictionaryAsync(d => d.DeviceId, ct);

        return tokens.Select(t =>
        {
            devices.TryGetValue(t.DeviceId ?? "", out var device);

            var platform = device?.Platform ?? "unknown";
            var model = device?.Model;
            var deviceName = model != null
                ? $"{model} ({platform})"
                : platform;

            return new SessionDto(
                Id: t.Id,
                DeviceName: deviceName,
                Platform: platform,
                Ip: device?.LastIpAddress,
                Location: device?.LastLocation,
                LastActiveAt: device?.LastActiveAt ?? t.CreatedAt,
                IsCurrent: t.DeviceId == req.CurrentDeviceId
            );
        }).ToList();
    }
}
