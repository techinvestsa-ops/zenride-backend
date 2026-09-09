using System.Security.Cryptography;
using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Auth.Dtos;
using Izigo.Application.Features.Auth.Helpers;
using Izigo.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Auth.Commands;

public record EnrollBiometricCommand(
    string UserId,
    string DeviceId,
    string PublicKeyOrSecret
) : IRequest<BiometricEnrollDto>;

public class EnrollBiometricHandler(IApplicationDbContext db)
    : IRequestHandler<EnrollBiometricCommand, BiometricEnrollDto>
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromDays(180);

    public async Task<BiometricEnrollDto> Handle(EnrollBiometricCommand req, CancellationToken ct)
    {
        // Revoke any existing biometric for this device
        var existing = await db.BiometricTokens
            .Where(b => b.UserId == req.UserId && b.DeviceId == req.DeviceId && b.IsActive)
            .ToListAsync(ct);
        foreach (var b in existing) b.IsActive = false;

        // Generate raw token — returned to client ONCE; only its hash is persisted
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var tokenHash = HashToken(rawToken);
        var expiresAt = DateTime.UtcNow.Add(TokenLifetime);

        db.BiometricTokens.Add(new BiometricToken
        {
            UserId = req.UserId,
            DeviceId = req.DeviceId,
            TokenHash = tokenHash,
            IsActive = true,
            ExpiresAt = expiresAt
        });

        await db.SaveChangesAsync(ct);
        return new BiometricEnrollDto(rawToken, expiresAt);
    }

    internal static string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes);
    }
}

public record BiometricLoginCommand(
    string BiometricToken,
    string DeviceId,
    string? IpAddress = null
) : IRequest<AuthBundleDto>;

public class BiometricLoginHandler(IApplicationDbContext db, ITokenService tokenSvc)
    : IRequestHandler<BiometricLoginCommand, AuthBundleDto>
{
    public async Task<AuthBundleDto> Handle(BiometricLoginCommand req, CancellationToken ct)
    {
        var hash = EnrollBiometricHandler.HashToken(req.BiometricToken);

        var bio = await db.BiometricTokens
            .FirstOrDefaultAsync(b =>
                b.TokenHash == hash &&
                b.DeviceId == req.DeviceId &&
                b.IsActive &&
                b.ExpiresAt > DateTime.UtcNow, ct)
            ?? throw new UnauthorizedAccessException("Biometric token invalid or expired.");

        var user = await db.Users.FindAsync([bio.UserId], ct)
            ?? throw new UnauthorizedAccessException("User not found.");

        if (user.Status == Domain.Enums.UserStatus.Blocked)
            throw new UnauthorizedAccessException("ACCOUNT_BLOCKED: Contact support.");

        var deviceDto = await db.UserDevices
            .Where(d => d.UserId == user.Id && d.DeviceId == req.DeviceId)
            .Select(d => new DeviceDto(d.DeviceId, d.Platform, d.AppVersion,
                d.OsVersion, d.Model, d.Locale, d.Timezone, d.FcmToken))
            .FirstOrDefaultAsync(ct);

        return await AuthBundleBuilder.BuildAsync(user, db, tokenSvc, deviceDto, req.IpAddress, ct);
    }
}

public record DisableBiometricCommand(string UserId, string DeviceId) : IRequest;

public class DisableBiometricHandler(IApplicationDbContext db) : IRequestHandler<DisableBiometricCommand>
{
    public async Task Handle(DisableBiometricCommand req, CancellationToken ct)
    {
        var tokens = await db.BiometricTokens
            .Where(b => b.UserId == req.UserId && b.DeviceId == req.DeviceId && b.IsActive)
            .ToListAsync(ct);
        foreach (var b in tokens) b.IsActive = false;
        await db.SaveChangesAsync(ct);
    }
}
