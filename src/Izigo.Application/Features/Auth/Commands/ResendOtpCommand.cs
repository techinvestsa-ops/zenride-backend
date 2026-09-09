using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Settings;
using Izigo.Application.Features.Auth.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Izigo.Application.Features.Auth.Commands;

public record ResendOtpCommand(string OtpToken, string? Channel) : IRequest<OtpResponseDto>;

public class ResendOtpHandler(
    IApplicationDbContext db,
    IOtpService otpSvc,
    IOptions<OtpSettings> otpOptions)
    : IRequestHandler<ResendOtpCommand, OtpResponseDto>
{
    public async Task<OtpResponseDto> Handle(ResendOtpCommand req, CancellationToken ct)
    {
        var cfg = otpOptions.Value;

        var record = await db.OtpRecords
            .FirstOrDefaultAsync(o => o.OtpToken == req.OtpToken && !o.IsUsed, ct)
            ?? throw new KeyNotFoundException("OTP session not found or already used.");

        if (record.ExpiresAt <= DateTime.UtcNow)
            throw new InvalidOperationException("CONFLICT: OTP session has expired. Request a new one.");

        if (record.CreatedAt.AddSeconds(cfg.ResendCooldownSeconds) > DateTime.UtcNow)
        {
            var wait = (int)(record.CreatedAt.AddSeconds(cfg.ResendCooldownSeconds) - DateTime.UtcNow).TotalSeconds;
            throw new InvalidOperationException($"CONFLICT: Please wait {wait} seconds before resending.");
        }

        var code = await otpSvc.GenerateAndSendAsync(record.Identifier, record.Purpose.ToString(), ct);
        record.CodeHash    = RequestOtpHandler.HashCode(code);
        record.AttemptCount = 0;
        record.UpdatedAt   = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var remaining = (int)(record.ExpiresAt - DateTime.UtcNow).TotalSeconds;
        return new OtpResponseDto(record.OtpToken, remaining, cfg.ResendCooldownSeconds, record.IsNewUser, code);
    }
}
