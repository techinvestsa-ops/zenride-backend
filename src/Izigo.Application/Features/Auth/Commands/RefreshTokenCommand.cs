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
    /// <summary>
    /// How long a just-rotated token may be presented again. On a bad mobile
    /// connection the server can rotate the token and the response never
    /// arrives; the app then retries with the old token and, without this
    /// window, the user is signed out.
    /// </summary>
    public static readonly TimeSpan ReuseGrace = TimeSpan.FromSeconds(30);

    public async Task<AuthBundleDto> Handle(RefreshTokenCommand req, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var stored = await db.RefreshTokens
            .FirstOrDefaultAsync(t => t.Token == req.RefreshToken && t.ExpiresAt > now, ct)
            ?? throw new UnauthorizedAccessException("Refresh token is invalid or expired.");

        if (stored.IsRevoked)
        {
            // Accept a retry only right after rotation, and only while the token
            // it was swapped for is still live. Logout, password change and
            // admin actions revoke that replacement, which closes the window.
            var replacement = stored.ReplacedAt > now - ReuseGrace && stored.ReplacedByTokenId != null
                ? await db.RefreshTokens.FirstOrDefaultAsync(t => t.Id == stored.ReplacedByTokenId, ct)
                : null;
            if (replacement is null || replacement.IsRevoked || replacement.ExpiresAt <= now)
                throw new UnauthorizedAccessException("Refresh token is invalid or expired.");
            replacement.IsRevoked = true;
        }

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

        var bundle = await AuthBundleBuilder.BuildAsync(user, db, tokenSvc, deviceDto, null, ct);

        stored.ReplacedAt = now;
        stored.ReplacedByTokenId = await db.RefreshTokens
            .Where(t => t.Token == bundle.RefreshToken)
            .Select(t => t.Id)
            .FirstOrDefaultAsync(ct);
        await db.SaveChangesAsync(ct);

        return bundle;
    }
}
