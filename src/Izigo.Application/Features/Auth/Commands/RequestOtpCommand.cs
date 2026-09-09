using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Settings;
using Izigo.Application.Common.Validators;
using Izigo.Application.Features.Auth.Dtos;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Izigo.Application.Features.Auth.Commands;

public record RequestOtpCommand(string Phone, string Role, string Purpose) : IRequest<OtpResponseDto>;

public class RequestOtpHandler(
    IApplicationDbContext db,
    IOtpService otpSvc,
    IOptions<OtpSettings> otpOptions)
    : IRequestHandler<RequestOtpCommand, OtpResponseDto>
{
    public async Task<OtpResponseDto> Handle(RequestOtpCommand req, CancellationToken ct)
    {
        var cfg = otpOptions.Value;
        PhoneValidator.Validate(req.Phone);

        if (!Enum.TryParse<UserRole>(req.Role, true, out var role))
            throw new ArgumentException("VALIDATION_ERROR: Invalid role.");

        if (!Enum.TryParse<OtpPurpose>(req.Purpose, true, out var purpose))
            throw new ArgumentException("VALIDATION_ERROR: Invalid purpose.");

        var recentCount = await db.OtpRecords
            .CountAsync(o => o.Identifier == req.Phone &&
                             o.CreatedAt > DateTime.UtcNow.AddHours(-1), ct);

        if (recentCount >= cfg.MaxPerHour)
            throw new InvalidOperationException(
                "CONFLICT: Too many OTP requests. Please wait before requesting another.");

        var isNewUser = !await db.Users
            .AnyAsync(u => u.Phone == req.Phone && u.Role == role, ct);

        var existing = await db.OtpRecords
            .Where(o => o.Identifier == req.Phone && o.Purpose == purpose &&
                        !o.IsUsed && o.ExpiresAt > DateTime.UtcNow)
            .ToListAsync(ct);
        foreach (var o in existing) o.IsUsed = true;

        var otpToken = Guid.CreateVersion7().ToString("N");
        var code = await otpSvc.GenerateAndSendAsync(req.Phone, req.Purpose, ct);

        db.OtpRecords.Add(new OtpRecord
        {
            OtpToken   = otpToken,
            Identifier = req.Phone,
            CodeHash   = HashCode(code),
            Purpose    = purpose,
            Role       = req.Role.ToLower(),
            IsNewUser  = isNewUser,
            ExpiresAt  = DateTime.UtcNow.AddSeconds(cfg.ExpirySeconds)
        });

        await db.SaveChangesAsync(ct);
        return new OtpResponseDto(otpToken, cfg.ExpirySeconds, cfg.ResendCooldownSeconds, isNewUser, code);
    }

    internal static string HashCode(string code)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(code));
        return Convert.ToHexString(bytes);
    }
}
