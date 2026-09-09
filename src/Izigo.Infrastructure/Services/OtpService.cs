using Izigo.Application.Common;
using Izigo.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace Izigo.Infrastructure.Services;

// Generates the OTP code and routes delivery:
//   identifier contains '@'  →  email OTP via IEmailService
//   everything else          →  SMS via ISmsService
public class OtpService(
    ISmsService sms,
    IEmailService email,
    ILogger<OtpService> logger) : IOtpService
{
    private static readonly Random Rng = new();

    public async Task<string> GenerateAndSendAsync(
        string identifier, string purpose, CancellationToken ct = default)
    {
        var code = Rng.Next(100_000, 999_999).ToString();

        if (identifier.Contains('@'))
            await SendByEmailAsync(identifier, code, ct);
        else
            await SendBySmsAsync(identifier, purpose, code, ct);

        return code;
    }

    // ── Delivery ──────────────────────────────────────────────────────────────

    private Task SendBySmsAsync(string phone, string purpose, string code, CancellationToken ct)
    {
        var message = BuildSmsMessage(purpose, code);
        logger.LogInformation("[OTP] Sending SMS to={Phone} purpose={Purpose}", phone, purpose);
        return sms.SendAsync(phone, message, ct);
    }

    private Task SendByEmailAsync(string emailAddr, string code, CancellationToken ct)
    {
        logger.LogInformation("[OTP] Sending email OTP to={Email}", emailAddr);
        return email.SendAsync(
            to:       emailAddr,
            toName:   emailAddr,
            subject:  "Your Zenride verification code",
            htmlBody: EmailTemplates.EmailVerification(code),
            ct:       ct);
    }

    // ── Message copy ──────────────────────────────────────────────────────────

    private static string BuildSmsMessage(string purpose, string code) =>
        purpose.ToLowerInvariant() switch
        {
            "password_reset"               => $"{code} is your Zenride password reset code. Valid 5 min. Do not share.",
            "verifyphone" or "verify_phone" => $"{code} is your Zenride phone verification code. Valid 10 min. Do not share.",
            _                              => $"{code} is your Zenride code. Valid 5 min. Do not share."
        };
}
