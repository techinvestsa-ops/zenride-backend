using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Auth.Dtos;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Auth.Helpers;

public static class AuthBundleBuilder
{
    private const int AppAccessExpirySeconds = 3600;
    private const int AppRefreshExpirySeconds = 2592000;

    public static async Task<AuthBundleDto> BuildAsync(
        User user,
        IApplicationDbContext db,
        ITokenService tokenSvc,
        DeviceDto? device,
        string? ipAddress,
        CancellationToken ct)
    {
        var wallet = await db.Wallets
            .Where(w => w.UserId == user.Id)
            .Select(w => new { w.Balance, w.Currency })
            .FirstOrDefaultAsync(ct);

        if (device != null)
            await UpsertDeviceAsync(user.Id, device, ipAddress, db, ct);

        var accessToken = tokenSvc.GenerateAccessToken(user);
        var rawRefreshToken = tokenSvc.GenerateRefreshToken();

        if (device != null)
        {
            var old = await db.RefreshTokens
                .Where(r => r.UserId == user.Id && r.DeviceId == device.DeviceId && !r.IsRevoked)
                .ToListAsync(ct);
            foreach (var t in old) t.IsRevoked = true;
        }

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            Token = rawRefreshToken,
            DeviceId = device?.DeviceId,
            ExpiresAt = DateTime.UtcNow.AddSeconds(AppRefreshExpirySeconds)
        });

        await db.SaveChangesAsync(ct);

        AuthDriverDto? driverDto = null;
        if (user.Role == UserRole.Driver)
        {
            var dp = await db.DriverProfiles
                .Include(d => d.Vehicles.Where(v => v.IsActive))
                .FirstOrDefaultAsync(d => d.UserId == user.Id, ct);

            if (dp != null)
            {
                var activeVehicle = dp.Vehicles.FirstOrDefault();
                driverDto = new AuthDriverDto(
                    KycStatus: dp.KycStatus.ToString().ToSnakeCase(),
                    OnboardingComplete: dp.OnboardingComplete,
                    IsOnline: dp.IsOnline,
                    VerticalsAllowed: dp.VerticalsAllowed
                        .Select(v => v.ToString().ToSnakeCase()).ToArray(),
                    Vehicle: activeVehicle == null ? null : new AuthVehicleDto(
                        activeVehicle.Type.ToString().ToLower(),
                        $"{activeVehicle.Make} {activeVehicle.Model}".Trim(),
                        activeVehicle.Plate)
                );
            }
        }

        return new AuthBundleDto(
            AccessToken: accessToken,
            TokenType: "Bearer",
            ExpiresIn: AppAccessExpirySeconds,
            RefreshToken: rawRefreshToken,
            RefreshExpiresIn: AppRefreshExpirySeconds,
            User: new AuthUserDto(
                Id: user.Id,
                Role: user.Role.ToString().ToLower(),
                FirstName: user.FirstName,
                LastName: user.LastName,
                Phone: user.Phone,
                PhoneVerified: user.PhoneVerified,
                Email: user.Email,
                EmailVerified: user.EmailVerified,
                PhotoUrl: user.PhotoUrl,
                Language: user.Language,
                CreatedAt: user.CreatedAt,
                Rating: user.Rating,
                WalletBalance: wallet?.Balance ?? 0,
                Currency: wallet?.Currency ?? "XOF"
            ),
            Driver: driverDto,
            NextStep: DetermineNextStep(user, driverDto)
        );
    }

    private static string DetermineNextStep(User user, AuthDriverDto? driver)
    {
        if (!user.PhoneVerified) return "verify_phone";
        if (string.IsNullOrWhiteSpace(user.FirstName) || string.IsNullOrWhiteSpace(user.LastName))
            return "complete_profile";
        if (user.Role == UserRole.Driver && driver?.KycStatus != "approved")
            return "kyc";
        return "home";
    }

    private static async Task UpsertDeviceAsync(
        string userId, DeviceDto device, string? ipAddress,
        IApplicationDbContext db, CancellationToken ct)
    {
        var existing = await db.UserDevices
            .FirstOrDefaultAsync(d => d.UserId == userId && d.DeviceId == device.DeviceId, ct);

        if (existing == null)
        {
            db.UserDevices.Add(new UserDevice
            {
                UserId = userId,
                DeviceId = device.DeviceId,
                Platform = device.Platform,
                AppVersion = device.AppVersion,
                OsVersion = device.OsVersion,
                Model = device.Model,
                Locale = device.Locale,
                Timezone = device.Timezone,
                FcmToken = device.FcmToken,
                LastActiveAt = DateTime.UtcNow,
                LastIpAddress = ipAddress
            });
        }
        else
        {
            existing.Platform = device.Platform;
            existing.AppVersion = device.AppVersion;
            existing.FcmToken = device.FcmToken ?? existing.FcmToken;
            existing.LastActiveAt = DateTime.UtcNow;
            existing.LastIpAddress = ipAddress ?? existing.LastIpAddress;
        }
    }
}

internal static class StringExtensions
{
    internal static string ToSnakeCase(this string s) =>
        string.Concat(s.Select((c, i) =>
            i > 0 && char.IsUpper(c)
                ? "_" + char.ToLower(c)
                : char.ToLower(c).ToString()));
}
