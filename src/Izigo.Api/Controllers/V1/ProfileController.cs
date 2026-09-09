using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Profile.Commands;
using Izigo.Application.Features.Profile.Dtos;
using Izigo.Application.Features.Profile.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Izigo.Api.Controllers.V1;

[Route("api/v1/me")]
[Authorize(Policy = "AppPolicy")]
public class ProfileController(ICurrentUserService currentUser) : BaseController
{
    // ── Profile ──────────────────────────────────────────────────────────────

    [HttpGet("")]
    public async Task<IActionResult> GetProfile(CancellationToken ct)
    {
        var result = await Mediator.Send(new GetProfileQuery(currentUser.UserId!), ct);
        return Ok(result);
    }

    [HttpPatch("")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest req, CancellationToken ct)
    {
        var result = await Mediator.Send(new UpdateProfileCommand(
            currentUser.UserId!,
            req.FirstName, req.LastName, req.Email,
            req.Language, req.Gender, req.DateOfBirth), ct);
        return Ok(result);
    }

    [HttpPost("photo")]
    [RequestSizeLimit(6 * 1024 * 1024)]     // 6 MB hard limit — handler enforces 5 MB business rule
    public async Task<IActionResult> UploadPhoto(IFormFile photo, CancellationToken ct)
    {
        if (photo == null || photo.Length == 0)
            return Unprocessable("VALIDATION_ERROR", "No file uploaded.");

        await using var stream = photo.OpenReadStream();
        var result = await Mediator.Send(new UploadPhotoCommand(
            currentUser.UserId!,
            stream,
            photo.FileName,
            photo.ContentType,
            photo.Length), ct);
        return Ok(result);
    }

    [Authorize(Policy = "RiderPolicy")]
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var result = await Mediator.Send(new GetStatsQuery(currentUser.UserId!), ct);
        return Ok(result);
    }

    // ── Phone ────────────────────────────────────────────────────────────────

    [HttpPost("phone/change")]
    public async Task<IActionResult> ChangePhone([FromBody] ChangePhoneRequest req, CancellationToken ct)
    {
        var result = await Mediator.Send(new ChangePhoneCommand(currentUser.UserId!, req.NewPhone), ct);
        return Ok(result);
    }

    // Contract note: "Then POST /me/phone/verify with code."
    [HttpPost("phone/verify")]
    public async Task<IActionResult> VerifyPhone([FromBody] VerifyPhoneRequest req, CancellationToken ct)
    {
        await Mediator.Send(new VerifyPhoneChangeCommand(currentUser.UserId!, req.OtpToken, req.Code), ct);
        return NoContent();
    }

    // ── Email ────────────────────────────────────────────────────────────────

    [HttpPost("email/verify/send")]
    public async Task<IActionResult> SendEmailVerification(CancellationToken ct)
    {
        await Mediator.Send(new SendEmailVerifyCommand(currentUser.UserId!), ct);
        return NoContent();
    }

    // Contract note: "Pairs with POST /me/email/verify (code)."
    [HttpPost("email/verify")]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest req, CancellationToken ct)
    {
        await Mediator.Send(new VerifyEmailCommand(currentUser.UserId!, req.Code), ct);
        return NoContent();
    }

    // ── Preferences ──────────────────────────────────────────────────────────

    [HttpGet("preferences")]
    public async Task<IActionResult> GetPreferences(CancellationToken ct)
    {
        var result = await Mediator.Send(new GetPreferencesQuery(currentUser.UserId!), ct);
        return Ok(result);
    }

    [HttpPatch("preferences")]
    public async Task<IActionResult> UpdatePreferences([FromBody] UpdatePreferencesRequest req, CancellationToken ct)
    {
        var result = await Mediator.Send(new UpdatePreferencesCommand(currentUser.UserId!, req), ct);
        return Ok(result);
    }

    // ── Emergency Contacts ───────────────────────────────────────────────────

    [Authorize(Policy = "RiderPolicy")]
    [HttpGet("emergency-contacts")]
    public async Task<IActionResult> GetEmergencyContacts(CancellationToken ct)
    {
        var result = await Mediator.Send(new GetEmergencyContactsQuery(currentUser.UserId!), ct);
        return Ok(result);
    }

    [Authorize(Policy = "RiderPolicy")]
    [HttpPost("emergency-contacts")]
    public async Task<IActionResult> AddEmergencyContact(
        [FromBody] AddEmergencyContactRequest req, CancellationToken ct)
    {
        var result = await Mediator.Send(new AddEmergencyContactCommand(
            currentUser.UserId!, req.Name, req.Phone, req.Relationship, req.NotifyOnTripStart), ct);
        return Created(result);
    }

    [Authorize(Policy = "RiderPolicy")]
    [HttpDelete("emergency-contacts/{id}")]
    public async Task<IActionResult> DeleteEmergencyContact(string id, CancellationToken ct)
    {
        await Mediator.Send(new DeleteEmergencyContactCommand(currentUser.UserId!, id), ct);
        return NoContent();
    }

    // ── Referral ─────────────────────────────────────────────────────────────

    [Authorize(Policy = "RiderPolicy")]
    [HttpGet("referral")]
    public async Task<IActionResult> GetReferral(CancellationToken ct)
    {
        var result = await Mediator.Send(new GetReferralQuery(currentUser.UserId!), ct);
        return Ok(result);
    }

    // ── Account Lifecycle ────────────────────────────────────────────────────

    [HttpPost("close-account")]
    public async Task<IActionResult> CloseAccount([FromBody] CloseAccountRequest req, CancellationToken ct)
    {
        await Mediator.Send(new CloseAccountCommand(currentUser.UserId!, req.Reason, req.PasswordOrOtp), ct);
        return NoContent();
    }

    [HttpGet("export")]
    public async Task<IActionResult> ExportData(CancellationToken ct)
    {
        var result = await Mediator.Send(new ExportDataCommand(currentUser.UserId!), ct);
        return Accepted(result);   // 202 — job queued
    }

    // Helper: 202 Accepted
    private IActionResult Accepted(object data)
        => StatusCode(202, Application.Common.Models.ApiResponse.Ok(data));
}
