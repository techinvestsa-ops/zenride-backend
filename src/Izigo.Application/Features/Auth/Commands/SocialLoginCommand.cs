using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Auth.Dtos;
using Izigo.Application.Features.Auth.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Auth.Commands;

public record SocialLoginCommand(
    string Provider,
    string IdToken,
    string Role,
    DeviceDto Device
) : IRequest<object>; // returns AuthBundleDto or SocialPendingDto

public class SocialLoginHandler(
    IApplicationDbContext db,
    ITokenService tokenSvc) : IRequestHandler<SocialLoginCommand, object>
{
    public async Task<object> Handle(SocialLoginCommand req, CancellationToken ct)
    {
        if (!Enum.TryParse<UserRole>(req.Role, true, out var role))
            throw new ArgumentException("VALIDATION_ERROR: Invalid role.");

        // TODO: verify id_token with Google/Apple SDK and extract email + sub
        // For now we parse the token naively — replace with real verification before production
        var (email, providerId) = ParseIdToken(req.Provider, req.IdToken);

        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("VALIDATION_ERROR: Could not extract email from social token.");

        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Email == email.ToLower() && u.Role == role, ct);

        if (user == null)
        {
            // Create a stub account — phone still needed
            user = new User
            {
                Email = email.ToLower(),
                EmailVerified = true,
                Role = role,
                Language = "fr"
            };
            db.Users.Add(user);
            db.Wallets.Add(new Wallet { UserId = user.Id, Currency = "XOF" });

            if (role == UserRole.Driver)
            {
                var profile = new DriverProfile { UserId = user.Id };
                db.DriverProfiles.Add(profile);
                db.DriverOnboardings.Add(new DriverOnboarding { DriverId = profile.Id });
                db.DriverWallets.Add(new DriverWallet { DriverId = user.Id });
            }

            await db.SaveChangesAsync(ct);

            // Phone binding required
            var bindToken = Guid.NewGuid().ToString("N");
            return new SocialPendingDto(NeedsPhone: true, SocialBindToken: bindToken);
        }

        if (string.IsNullOrWhiteSpace(user.Phone))
            return new SocialPendingDto(NeedsPhone: true, SocialBindToken: Guid.NewGuid().ToString("N"));

        if (user.Status == UserStatus.Blocked)
            throw new UnauthorizedAccessException("ACCOUNT_BLOCKED: Contact support.");

        return await AuthBundleBuilder.BuildAsync(user, db, tokenSvc, req.Device, null, ct);
    }

    // Minimal JWT body parser — not a security check, just field extraction
    private static (string email, string sub) ParseIdToken(string provider, string idToken)
    {
        try
        {
            var parts = idToken.Split('.');
            if (parts.Length < 2) return (string.Empty, string.Empty);

            var payload = parts[1];
            var padded = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var email = doc.RootElement.TryGetProperty("email", out var e) ? e.GetString() ?? "" : "";
            var sub = doc.RootElement.TryGetProperty("sub", out var s) ? s.GetString() ?? "" : "";
            return (email, sub);
        }
        catch
        {
            return (string.Empty, string.Empty);
        }
    }
}
