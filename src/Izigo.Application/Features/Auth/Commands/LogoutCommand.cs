using Izigo.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Auth.Commands;

public record LogoutCommand(string UserId, string? DeviceId) : IRequest;

public class LogoutHandler(IApplicationDbContext db) : IRequestHandler<LogoutCommand>
{
    public async Task Handle(LogoutCommand req, CancellationToken ct)
    {
        // Revoke all refresh tokens for this device
        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == req.UserId &&
                        (req.DeviceId == null || t.DeviceId == req.DeviceId) &&
                        !t.IsRevoked)
            .ToListAsync(ct);

        foreach (var t in tokens) t.IsRevoked = true;

        // Clear FCM token for the device so push stops
        if (!string.IsNullOrWhiteSpace(req.DeviceId))
        {
            var device = await db.UserDevices
                .FirstOrDefaultAsync(d => d.UserId == req.UserId && d.DeviceId == req.DeviceId, ct);
            if (device != null) device.FcmToken = null;
        }

        await db.SaveChangesAsync(ct);
    }
}
