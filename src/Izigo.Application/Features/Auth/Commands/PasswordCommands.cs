using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Settings;
using Izigo.Application.Features.Auth.Dtos;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Izigo.Application.Features.Auth.Commands;

// ─── Forgot Password ─────────────────────────────────────────────────────────

public record ForgotPasswordCommand(string Identifier, string Role) : IRequest<ForgotPasswordDto>;

public class ForgotPasswordHandler(
    IApplicationDbContext db,
    IOtpService otpSvc,
    IOptions<OtpSettings> otpOptions)
    : IRequestHandler<ForgotPasswordCommand, ForgotPasswordDto>
{
    public async Task<ForgotPasswordDto> Handle(ForgotPasswordCommand req, CancellationToken ct)
    {
        var expiry = otpOptions.Value.PasswordResetExpirySeconds;

        if (!Enum.TryParse<UserRole>(req.Role, true, out var role))
            throw new ArgumentException("VALIDATION_ERROR: Invalid role.");

        var identifier = req.Identifier.Trim().ToLower();
        var user = await db.Users.FirstOrDefaultAsync(u =>
            u.Role == role &&
            (u.Phone == req.Identifier ||
             (u.Email != null && u.Email.ToLower() == identifier)), ct);

        // Always 200 — never reveal whether the identifier exists
        if (user == null)
            return new ForgotPasswordDto(Guid.CreateVersion7().ToString("N"), expiry);

        var existing = await db.OtpRecords
            .Where(o => o.Identifier == user.Phone && !o.IsUsed && o.ExpiresAt > DateTime.UtcNow)
            .ToListAsync(ct);
        foreach (var o in existing) o.IsUsed = true;

        var resetToken = Guid.CreateVersion7().ToString("N");
        var code = await otpSvc.GenerateAndSendAsync(user.Phone, "password_reset", ct);

        db.OtpRecords.Add(new OtpRecord
        {
            OtpToken   = resetToken,
            Identifier = user.Phone,
            CodeHash   = RequestOtpHandler.HashCode(code),
            Purpose    = OtpPurpose.Login,
            Role       = req.Role.ToLower(),
            ExpiresAt  = DateTime.UtcNow.AddSeconds(expiry)
        });

        await db.SaveChangesAsync(ct);
        return new ForgotPasswordDto(resetToken, expiry);
    }
}

// ─── Verify Reset Code ────────────────────────────────────────────────────────

public record VerifyResetCodeCommand(string ResetToken, string Code) : IRequest<VerifyResetCodeDto>;

public class VerifyResetCodeHandler(IApplicationDbContext db)
    : IRequestHandler<VerifyResetCodeCommand, VerifyResetCodeDto>
{
    public async Task<VerifyResetCodeDto> Handle(VerifyResetCodeCommand req, CancellationToken ct)
    {
        var record = await db.OtpRecords
            .FirstOrDefaultAsync(o =>
                o.OtpToken == req.ResetToken && !o.IsUsed && o.ExpiresAt > DateTime.UtcNow, ct)
            ?? throw new KeyNotFoundException("Reset token invalid or expired.");

        var valid = string.Equals(
            record.CodeHash, RequestOtpHandler.HashCode(req.Code),
            StringComparison.OrdinalIgnoreCase);

        if (!valid)
        {
            record.AttemptCount++;
            await db.SaveChangesAsync(ct);
            throw new ArgumentException("VALIDATION_ERROR: Invalid code.");
        }

        return new VerifyResetCodeDto(true);
    }
}

// ─── Reset Password ───────────────────────────────────────────────────────────

public record ResetPasswordCommand(
    string ResetToken, string Code,
    string Password, string PasswordConfirmation
) : IRequest;

public class ResetPasswordHandler(IApplicationDbContext db, IPasswordHasher hasher)
    : IRequestHandler<ResetPasswordCommand>
{
    public async Task Handle(ResetPasswordCommand req, CancellationToken ct)
    {
        if (req.Password != req.PasswordConfirmation)
            throw new ArgumentException("VALIDATION_ERROR: Passwords do not match.");

        if (req.Password.Length < 8)
            throw new ArgumentException("VALIDATION_ERROR: Password must be at least 8 characters.");

        var record = await db.OtpRecords
            .FirstOrDefaultAsync(o =>
                o.OtpToken == req.ResetToken && !o.IsUsed && o.ExpiresAt > DateTime.UtcNow, ct)
            ?? throw new KeyNotFoundException("Reset token invalid or expired.");

        if (!string.Equals(record.CodeHash, RequestOtpHandler.HashCode(req.Code),
                StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("VALIDATION_ERROR: Invalid code.");

        var role = Enum.Parse<UserRole>(record.Role!, true);
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Phone == record.Identifier && u.Role == role, ct)
            ?? throw new KeyNotFoundException("User not found.");

        user.PasswordHash = hasher.Hash(req.Password);
        record.IsUsed = true;

        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == user.Id && !t.IsRevoked)
            .ToListAsync(ct);
        foreach (var t in tokens) t.IsRevoked = true;

        await db.SaveChangesAsync(ct);
    }
}

// ─── Change Password (authenticated) ─────────────────────────────────────────

public record ChangePasswordCommand(
    string UserId,
    string CurrentPassword,
    string Password,
    string PasswordConfirmation
) : IRequest;

public class ChangePasswordHandler(IApplicationDbContext db, IPasswordHasher hasher)
    : IRequestHandler<ChangePasswordCommand>
{
    public async Task Handle(ChangePasswordCommand req, CancellationToken ct)
    {
        if (req.Password != req.PasswordConfirmation)
            throw new ArgumentException("VALIDATION_ERROR: Passwords do not match.");

        if (req.Password.Length < 8)
            throw new ArgumentException("VALIDATION_ERROR: Password must be at least 8 characters.");

        var user = await db.Users.FindAsync([req.UserId], ct)
            ?? throw new KeyNotFoundException("User not found.");

        if (string.IsNullOrWhiteSpace(user.PasswordHash) ||
            !hasher.Verify(req.CurrentPassword, user.PasswordHash))
            throw new UnauthorizedAccessException("Current password is incorrect.");

        user.PasswordHash = hasher.Hash(req.Password);
        await db.SaveChangesAsync(ct);
    }
}
