using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Settings;
using Izigo.Application.Features.Auth.Commands;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Izigo.Application.Features.Profile.Commands;

// ── Send verification code to current email ───────────────────────────────────

public record SendEmailVerifyCommand(string UserId) : IRequest;

public class SendEmailVerifyHandler(
    IApplicationDbContext db,
    IOtpService otpSvc,
    IOptions<OtpSettings> otpOptions)
    : IRequestHandler<SendEmailVerifyCommand>
{
    public async Task Handle(SendEmailVerifyCommand req, CancellationToken ct)
    {
        var expiry = otpOptions.Value.EmailExpirySeconds;

        var user = await db.Users.FindAsync([req.UserId], ct)
            ?? throw new KeyNotFoundException("User not found.");

        if (string.IsNullOrWhiteSpace(user.Email))
            throw new InvalidOperationException("CONFLICT: No email address on this account.");

        if (user.EmailVerified)
            throw new InvalidOperationException("CONFLICT: Email is already verified.");

        var existing = await db.OtpRecords
            .Where(o => o.Identifier == user.Email &&
                        o.Purpose == Domain.Enums.OtpPurpose.VerifyEmail &&
                        !o.IsUsed)
            .ToListAsync(ct);
        foreach (var o in existing) o.IsUsed = true;

        var otpToken = Guid.CreateVersion7().ToString("N");
        var code = await otpSvc.GenerateAndSendAsync(user.Email, "email_verify", ct);

        db.OtpRecords.Add(new Domain.Entities.OtpRecord
        {
            OtpToken   = otpToken,
            Identifier = user.Email,
            CodeHash   = RequestOtpHandler.HashCode(code),
            Purpose    = Domain.Enums.OtpPurpose.VerifyEmail,
            Role       = user.Role.ToString().ToLower(),
            ExpiresAt  = DateTime.UtcNow.AddSeconds(expiry)
        });

        await db.SaveChangesAsync(ct);
    }
}

// ── Submit verification code ──────────────────────────────────────────────────

public record VerifyEmailCommand(string UserId, string Code) : IRequest;

public class VerifyEmailHandler(IApplicationDbContext db) : IRequestHandler<VerifyEmailCommand>
{
    public async Task Handle(VerifyEmailCommand req, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([req.UserId], ct)
            ?? throw new KeyNotFoundException("User not found.");

        if (string.IsNullOrWhiteSpace(user.Email))
            throw new InvalidOperationException("CONFLICT: No email address on this account.");

        var record = await db.OtpRecords
            .Where(o => o.Identifier == user.Email &&
                        o.Purpose == Domain.Enums.OtpPurpose.VerifyEmail &&
                        !o.IsUsed &&
                        o.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("No pending email verification found or it has expired.");

        if (!string.Equals(
                record.CodeHash, RequestOtpHandler.HashCode(req.Code),
                StringComparison.OrdinalIgnoreCase))
        {
            record.AttemptCount++;
            await db.SaveChangesAsync(ct);
            throw new ArgumentException("VALIDATION_ERROR: Invalid code.");
        }

        user.EmailVerified = true;
        record.IsUsed = true;
        await db.SaveChangesAsync(ct);
    }
}
