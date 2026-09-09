using System.Text.Json;
using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Auth.Commands;
using Izigo.Application.Features.Auth.Dtos;
using Izigo.Application.Features.Auth.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Izigo.Api.Controllers.V1;

[Route("api/v1/auth")]
public class AuthController(
    ICurrentUserService currentUser,
    IIdempotencyService idempotency) : BaseController
{
    // ── OTP ──────────────────────────────────────────────────────────────────

    [AllowAnonymous]
    [EnableRateLimiting("otp")]
    [HttpPost("otp/request")]
    public async Task<IActionResult> RequestOtp(
        [FromBody] RequestOtpRequest req, CancellationToken ct)
    {
        var result = await Mediator.Send(
            new RequestOtpCommand(req.Phone, req.Role, req.Purpose), ct);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpPost("otp/verify")]
    public async Task<IActionResult> VerifyOtp(
        [FromBody] VerifyOtpRequest req, CancellationToken ct)
    {
        var result = await Mediator.Send(
            new VerifyOtpCommand(req.OtpToken, req.Code, req.Device, currentUser.IpAddress), ct);
        return Ok(result);
    }

    [AllowAnonymous]
    [EnableRateLimiting("otp")]
    [HttpPost("otp/resend")]
    public async Task<IActionResult> ResendOtp(
        [FromBody] ResendOtpRequest req, CancellationToken ct)
    {
        var result = await Mediator.Send(new ResendOtpCommand(req.OtpToken, req.Channel), ct);
        return Ok(result);
    }

    // ── Registration & Login ─────────────────────────────────────────────────

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody] RegisterRequest req,
        CancellationToken ct)
    {
        // Replay cached response if the same key is submitted again
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var cached = await idempotency.GetAsync(idempotencyKey, null, ct);
            if (cached != null)
                return StatusCode(cached.StatusCode,
                    JsonSerializer.Deserialize<object>(cached.ResponseJson));
        }

        var result = await Mediator.Send(new RegisterCommand(
            req.FirstName, req.LastName, req.Phone,
            req.Email, req.Password, req.Role,
            req.ReferralCode, req.Language, req.Device,
            currentUser.IpAddress), ct);

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var body = JsonSerializer.Serialize(
                Application.Common.Models.ApiResponse<object>.Ok(result),
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower
                });
            await idempotency.SaveAsync(idempotencyKey, null, "register", body, 201, ct);
        }

        return Created(result);
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest req, CancellationToken ct)
    {
        var result = await Mediator.Send(new LoginCommand(
            req.ResolvedIdentifier, req.Password, req.Role,
            req.Device, currentUser.IpAddress), ct);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpPost("social")]
    public async Task<IActionResult> SocialAuth(
        [FromBody] SocialLoginRequest req, CancellationToken ct)
    {
        var result = await Mediator.Send(
            new SocialLoginCommand(req.Provider, req.IdToken, req.Role, req.Device), ct);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshTokenRequest req, CancellationToken ct)
    {
        var result = await Mediator.Send(new RefreshTokenCommand(req.RefreshToken), ct);
        return Ok(result);
    }

    [Authorize(Policy = "AppPolicy")]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(
        [FromHeader(Name = "X-Device-Id")] string? deviceId,
        CancellationToken ct)
    {
        await Mediator.Send(new LogoutCommand(currentUser.UserId!, deviceId), ct);
        return NoContent();
    }

    // ── Session Management ───────────────────────────────────────────────────

    [Authorize(Policy = "AppPolicy")]
    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions(
        [FromHeader(Name = "X-Device-Id")] string? deviceId,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new GetSessionsQuery(currentUser.UserId!, deviceId), ct);
        return Ok(result);
    }

    [Authorize(Policy = "AppPolicy")]
    [HttpDelete("sessions/{id}")]
    public async Task<IActionResult> DeleteSession(string id, CancellationToken ct)
    {
        await Mediator.Send(new RevokeSessionCommand(currentUser.UserId!, id), ct);
        return NoContent();
    }

    [Authorize(Policy = "AppPolicy")]
    [HttpPost("sessions/revoke-others")]
    public async Task<IActionResult> RevokeOtherSessions(
        [FromHeader(Name = "X-Device-Id")] string? deviceId,
        CancellationToken ct)
    {
        await Mediator.Send(new RevokeOtherSessionsCommand(currentUser.UserId!, deviceId ?? ""), ct);
        return NoContent();
    }

    // ── Availability Check ───────────────────────────────────────────────────

    [AllowAnonymous]
    [EnableRateLimiting("check_avail")]
    [HttpPost("check-availability")]
    public async Task<IActionResult> CheckAvailability(
        [FromBody] CheckAvailabilityRequest req, CancellationToken ct)
    {
        var result = await Mediator.Send(
            new CheckAvailabilityCommand(req.Phone, req.Email, req.Role), ct);
        return Ok(result);
    }

    // ── Password Management ──────────────────────────────────────────────────

    [AllowAnonymous]
    [EnableRateLimiting("otp")]
    [HttpPost("password/forgot")]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest req, CancellationToken ct)
    {
        var result = await Mediator.Send(
            new ForgotPasswordCommand(req.Identifier, req.Role), ct);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpPost("password/verify-code")]
    public async Task<IActionResult> VerifyPasswordCode(
        [FromBody] VerifyResetCodeRequest req, CancellationToken ct)
    {
        var result = await Mediator.Send(
            new VerifyResetCodeCommand(req.ResetToken, req.Code), ct);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpPost("password/reset")]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest req, CancellationToken ct)
    {
        await Mediator.Send(new ResetPasswordCommand(
            req.ResetToken, req.Code, req.Password, req.PasswordConfirmation), ct);
        return NoContent();
    }

    [Authorize(Policy = "AppPolicy")]
    [HttpPost("password/change")]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest req, CancellationToken ct)
    {
        await Mediator.Send(new ChangePasswordCommand(
            currentUser.UserId!, req.CurrentPassword, req.Password, req.PasswordConfirmation), ct);
        return NoContent();
    }

    // ── Biometric ────────────────────────────────────────────────────────────

    [Authorize(Policy = "AppPolicy")]
    [HttpPost("biometric/enroll")]
    public async Task<IActionResult> EnrollBiometric(
        [FromBody] EnrollBiometricRequest req,
        [FromHeader(Name = "X-Device-Id")] string deviceId,
        CancellationToken ct)
    {
        var result = await Mediator.Send(
            new EnrollBiometricCommand(currentUser.UserId!, deviceId, req.PublicKeyOrSecret), ct);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpPost("biometric/login")]
    public async Task<IActionResult> BiometricLogin(
        [FromBody] BiometricLoginRequest req, CancellationToken ct)
    {
        var result = await Mediator.Send(
            new BiometricLoginCommand(req.BiometricToken, req.DeviceId, currentUser.IpAddress), ct);
        return Ok(result);
    }

    [Authorize(Policy = "AppPolicy")]
    [HttpDelete("biometric")]
    public async Task<IActionResult> DeleteBiometric(
        [FromHeader(Name = "X-Device-Id")] string deviceId,
        CancellationToken ct)
    {
        await Mediator.Send(new DisableBiometricCommand(currentUser.UserId!, deviceId), ct);
        return NoContent();
    }
}
