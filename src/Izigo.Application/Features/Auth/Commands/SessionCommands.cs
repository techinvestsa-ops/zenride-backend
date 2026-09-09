using Izigo.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Auth.Commands;

// Revoke a specific session
public record RevokeSessionCommand(string UserId, string SessionId) : IRequest;

public class RevokeSessionHandler(IApplicationDbContext db) : IRequestHandler<RevokeSessionCommand>
{
    public async Task Handle(RevokeSessionCommand req, CancellationToken ct)
    {
        var token = await db.RefreshTokens
            .FirstOrDefaultAsync(t => t.Id == req.SessionId && t.UserId == req.UserId, ct)
            ?? throw new KeyNotFoundException("Session not found.");

        token.IsRevoked = true;
        await db.SaveChangesAsync(ct);
    }
}

// Revoke all sessions except the current device
public record RevokeOtherSessionsCommand(string UserId, string CurrentDeviceId) : IRequest;

public class RevokeOtherSessionsHandler(IApplicationDbContext db) : IRequestHandler<RevokeOtherSessionsCommand>
{
    public async Task Handle(RevokeOtherSessionsCommand req, CancellationToken ct)
    {
        var others = await db.RefreshTokens
            .Where(t => t.UserId == req.UserId &&
                        t.DeviceId != req.CurrentDeviceId &&
                        !t.IsRevoked)
            .ToListAsync(ct);

        foreach (var t in others) t.IsRevoked = true;
        await db.SaveChangesAsync(ct);
    }
}
