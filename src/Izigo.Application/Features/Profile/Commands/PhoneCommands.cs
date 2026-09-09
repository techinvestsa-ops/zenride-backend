using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Settings;
using Izigo.Application.Common.Validators;
using Izigo.Application.Features.Auth.Commands;
using Izigo.Application.Features.Profile.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Izigo.Application.Features.Profile.Commands;

// ── Start phone change ────────────────────────────────────────────────────────

public record ChangePhoneCommand(string UserId, string NewPhone) : IRequest<ChangePhoneResult>;

public class ChangePhoneHandler(
    IApplicationDbContext db,
    IOtpService otpSvc,
    IOptions<OtpSettings> otpOptions)
    : IRequestHandler<ChangePhoneCommand, ChangePhoneResult>
{
    public async Task<ChangePhoneResult> Handle(ChangePhoneCommand req, CancellationToken ct)
    {
        var expiry = otpOptions.Value.ExpirySeconds;
        PhoneValidator.Validate(req.NewPhone, "new_phone");

        var user = await db.Users.FindAsync([req.UserId], ct)
            ?? throw new KeyNotFoundException("User not found.");

        if (await db.Users.AnyAsync(
                u => u.Phone == req.NewPhone && u.Role == user.Role && u.Id != req.UserId, ct))
            throw new ArgumentException("VALIDATION_ERROR: Phone number is already registered.");

        var otpToken = Guid.CreateVersion7().ToString("N");
        var code = await otpSvc.GenerateAndSendAsync(req.NewPhone, "verify_phone", ct);

        db.OtpRecords.Add(new Domain.Entities.OtpRecord
        {
            OtpToken   = otpToken,
            Identifier = req.NewPhone,
            CodeHash   = RequestOtpHandler.HashCode(code),
            Purpose    = Domain.Enums.OtpPurpose.VerifyPhone,
            Role       = user.Role.ToString().ToLower(),
            ExpiresAt  = DateTime.UtcNow.AddSeconds(expiry),
            IsNewUser  = false
        });

        await db.SaveChangesAsync(ct);
        return new ChangePhoneResult(otpToken, expiry);
    }
}

// ── Verify new phone ──────────────────────────────────────────────────────────

public record VerifyPhoneChangeCommand(string UserId, string OtpToken, string Code) : IRequest;

public class VerifyPhoneChangeHandler(IApplicationDbContext db)
    : IRequestHandler<VerifyPhoneChangeCommand>
{
    public async Task Handle(VerifyPhoneChangeCommand req, CancellationToken ct)
    {
        var record = await db.OtpRecords
            .FirstOrDefaultAsync(o =>
                o.OtpToken == req.OtpToken &&
                o.Purpose == Domain.Enums.OtpPurpose.VerifyPhone &&
                !o.IsUsed &&
                o.ExpiresAt > DateTime.UtcNow, ct)
            ?? throw new KeyNotFoundException("OTP session not found or expired.");

        if (!string.Equals(record.CodeHash, RequestOtpHandler.HashCode(req.Code),
                StringComparison.OrdinalIgnoreCase))
        {
            record.AttemptCount++;
            await db.SaveChangesAsync(ct);
            throw new ArgumentException("VALIDATION_ERROR: Invalid code.");
        }

        var user = await db.Users.FindAsync([req.UserId], ct)
            ?? throw new KeyNotFoundException("User not found.");

        user.Phone = record.Identifier;
        user.PhoneVerified = true;
        record.IsUsed = true;

        await db.SaveChangesAsync(ct);
    }
}
