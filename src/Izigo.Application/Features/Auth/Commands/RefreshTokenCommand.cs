using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Auth.Dtos;
using Izigo.Application.Features.Auth.Helpers;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Auth.Commands;

public record RefreshTokenCommand(string RefreshToken) : IRequest<AuthBundleDto>;

public class RefreshTokenHandler(
    IApplicationDbContext db,
    ITokenService tokenSvc) : IRequestHandler<RefreshTokenCommand, AuthBundleDto>
{
    public async Task<AuthBundleDto> Handle(RefreshTokenCommand req, CancellationToken ct)
    {
        var stored = await db.RefreshTokens
            .FirstOrDefaultAsync(t =>
                t.Token == req.RefreshToken &&
                !t.IsRevoked &&
                t.ExpiresAt > DateTime.UtcNow, ct)
            ?? throw new UnauthorizedAccessException("Refresh token is invalid or expired.");

        stored.IsRevoked = true;

        var user = await db.Users.FindAsync([stored.UserId], ct)
            ?? throw new UnauthorizedAccessException("User not found.");

        if (user.Status == Domain.Enums.UserStatus.Blocked)
            throw new UnauthorizedAccessException("ACCOUNT_BLOCKED: Contact support.");

        // Build bundle — AuthBundleBuilder issues a new refresh token
        var deviceDto = stored.DeviceId == null ? null :
            await db.UserDevices
                .Where(d => d.UserId == user.Id && d.DeviceId == stored.DeviceId)
                .Select(d => new DeviceDto(d.DeviceId, d.Platform, d.AppVersion,
                    d.OsVersion, d.Model, d.Locale, d.Timezone, d.FcmToken))
                .FirstOrDefaultAsync(ct);

        return await AuthBundleBuilder.BuildAsync(user, db, tokenSvc, deviceDto, null, ct);
    }
}
