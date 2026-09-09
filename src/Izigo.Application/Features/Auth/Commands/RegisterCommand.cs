using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Validators;
using Izigo.Application.Features.Auth.Dtos;
using Izigo.Application.Features.Auth.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Auth.Commands;

public record RegisterCommand(
    string FirstName,
    string LastName,
    string Phone,
    string? Email,
    string? Password,
    string Role,
    string? ReferralCode,
    string Language,
    DeviceDto Device,
    string? IpAddress = null
) : IRequest<AuthBundleDto>;

public class RegisterHandler(
    IApplicationDbContext db,
    ITokenService tokenSvc,
    IPasswordHasher hasher) : IRequestHandler<RegisterCommand, AuthBundleDto>
{
    public async Task<AuthBundleDto> Handle(RegisterCommand req, CancellationToken ct)
    {
        PhoneValidator.Validate(req.Phone);

        if (!Enum.TryParse<UserRole>(req.Role, true, out var role))
            throw new ArgumentException("VALIDATION_ERROR: Invalid role.");

        if (await db.Users.AnyAsync(u => u.Phone == req.Phone && u.Role == role, ct))
            throw new ArgumentException("VALIDATION_ERROR: Phone already registered.");

        if (!string.IsNullOrWhiteSpace(req.Email) &&
            await db.Users.AnyAsync(u => u.Email == req.Email.Trim().ToLower() && u.Role == role, ct))
            throw new ArgumentException("VALIDATION_ERROR: Email already registered.");

        string? referrerId = null;
        if (!string.IsNullOrWhiteSpace(req.ReferralCode))
            referrerId = await db.Users
                .Where(u => u.ReferralCode == req.ReferralCode)
                .Select(u => u.Id)
                .FirstOrDefaultAsync(ct);

        var user = new User
        {
            FirstName = req.FirstName.Trim(),
            LastName = req.LastName.Trim(),
            Phone = req.Phone,
            PhoneVerified = false,
            Email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim().ToLower(),
            PasswordHash = string.IsNullOrWhiteSpace(req.Password) ? null : hasher.Hash(req.Password),
            Role = role,
            Language = req.Language,
            ReferralCode = GenerateReferralCode(),
            ReferredByUserId = referrerId
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
        return await AuthBundleBuilder.BuildAsync(user, db, tokenSvc, req.Device, req.IpAddress, ct);
    }

    private static string GenerateReferralCode()
        => Guid.CreateVersion7().ToString("N")[..8].ToUpper();
}
