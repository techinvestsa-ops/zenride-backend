using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Auth.Dtos;
using Izigo.Application.Features.Auth.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Auth.Commands;

public record VerifyOtpCommand(
    string OtpToken,
    string Code,
    DeviceDto Device,
    string? IpAddress = null
) : IRequest<AuthBundleDto>;

public class VerifyOtpHandler(
    IApplicationDbContext db,
    ITokenService tokenSvc) : IRequestHandler<VerifyOtpCommand, AuthBundleDto>
{
    private const int MaxAttempts = 5;

    public async Task<AuthBundleDto> Handle(VerifyOtpCommand req, CancellationToken ct)
    {
        var record = await db.OtpRecords
            .FirstOrDefaultAsync(o => o.OtpToken == req.OtpToken, ct)
            ?? throw new KeyNotFoundException("Invalid or expired OTP token.");

        if (record.IsUsed || record.ExpiresAt <= DateTime.UtcNow)
            throw new InvalidOperationException("CONFLICT: OTP has expired.");

        if (record.AttemptCount >= MaxAttempts)
            throw new InvalidOperationException(
                "CONFLICT: Too many incorrect attempts. Request a new code.");

        if (!string.Equals(record.CodeHash, RequestOtpHandler.HashCode(req.Code),
                StringComparison.OrdinalIgnoreCase))
        {
            record.AttemptCount++;
            await db.SaveChangesAsync(ct);
            var remaining = MaxAttempts - record.AttemptCount;
            throw new ArgumentException(
                $"VALIDATION_ERROR: Invalid code. {remaining} attempt(s) remaining.");
        }

        record.IsUsed = true;
        await db.SaveChangesAsync(ct);

        var role = Enum.Parse<UserRole>(record.Role!, true);
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Phone == record.Identifier && u.Role == role, ct);

        if (user == null)
        {
            user = new User
            {
                Phone = record.Identifier,
                PhoneVerified = true,
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
        }
        else
        {
            user.PhoneVerified = true;
            await db.SaveChangesAsync(ct);
        }

        if (user.Status == UserStatus.Suspended)
            throw new InvalidOperationException(
                $"CONFLICT: Account suspended. {user.SuspensionReason}");
        if (user.Status == UserStatus.Blocked)
            throw new InvalidOperationException("CONFLICT: Account blocked. Contact support.");

        return await AuthBundleBuilder.BuildAsync(user, db, tokenSvc, req.Device, req.IpAddress, ct);
    }
}
