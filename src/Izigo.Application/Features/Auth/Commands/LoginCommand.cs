using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Auth.Dtos;
using Izigo.Application.Features.Auth.Helpers;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Auth.Commands;

public record LoginCommand(
    string Identifier,
    string Password,
    string Role,
    DeviceDto Device,
    string? IpAddress = null
) : IRequest<AuthBundleDto>;

public class LoginHandler(
    IApplicationDbContext db,
    ITokenService tokenSvc,
    IPasswordHasher hasher) : IRequestHandler<LoginCommand, AuthBundleDto>
{
    public async Task<AuthBundleDto> Handle(LoginCommand req, CancellationToken ct)
    {
        if (!Enum.TryParse<UserRole>(req.Role, true, out var role))
            throw new ArgumentException("VALIDATION_ERROR: Invalid role.");

        var identifier = req.Identifier.Trim().ToLower();

        var user = await db.Users.FirstOrDefaultAsync(u =>
            u.Role == role &&
            (u.Phone == req.Identifier ||
             (u.Email != null && u.Email.ToLower() == identifier)), ct);

        // Always run hash to prevent timing attacks
        var hashToCheck = user?.PasswordHash ?? "$dummy$";
        var passwordValid = hasher.Verify(req.Password, hashToCheck);

        if (user == null || !passwordValid)
            throw new UnauthorizedAccessException("Invalid credentials.");

        if (user.Status == UserStatus.Suspended)
            throw new UnauthorizedAccessException($"ACCOUNT_SUSPENDED: {user.SuspensionReason}");

        if (user.Status == UserStatus.Blocked)
            throw new UnauthorizedAccessException("ACCOUNT_BLOCKED: Contact support.");

        return await AuthBundleBuilder.BuildAsync(user, db, tokenSvc, req.Device, req.IpAddress, ct);
    }
}
