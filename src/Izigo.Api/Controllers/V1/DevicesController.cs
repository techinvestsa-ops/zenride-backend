using Izigo.Application.Features.Devices.Commands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Izigo.Api.Controllers.V1;

/// <summary>
/// Manages device registrations for push-notification delivery and
/// driver-side diagnostic reporting.
/// </summary>
[Route("api/v1/devices")]
[Authorize(Policy = "AppPolicy")]
public class DevicesController : BaseController
{
    public record RegisterDeviceBody(
        string FcmToken, string DeviceId, string Platform,
        string? AppVersion, string? OsVersion, string? Model,
        string? Locale, string? Timezone);

    public record SubmitDiagnosticsBody(
        int? BatteryLevel, bool? LocationPermission,
        bool? BackgroundPermission, bool? MockLocationDetected);

    /// <summary>Registers or updates a device token for push notifications.</summary>
    [HttpPost("")]
    public async Task<IActionResult> RegisterDevice([FromBody] RegisterDeviceBody body)
    {
        await Mediator.Send(new RegisterDeviceCommand(
            CurrentUserId, body.FcmToken, body.DeviceId, body.Platform,
            body.AppVersion, body.OsVersion, body.Model, body.Locale, body.Timezone));
        return NoContent();
    }

    /// <summary>Unregisters a device token, stopping push notifications to that device.</summary>
    [HttpDelete("{deviceId}")]
    public async Task<IActionResult> UnregisterDevice(string deviceId)
    {
        await Mediator.Send(new UnregisterDeviceCommand(CurrentUserId, deviceId));
        return NoContent();
    }

    /// <summary>Submits a driver-side device diagnostics payload (battery, GPS accuracy, etc.).</summary>
    [Authorize(Policy = "DriverPolicy")]
    [HttpPost("{deviceId}/diagnostics")]
    public async Task<IActionResult> SubmitDiagnostics(string deviceId, [FromBody] SubmitDiagnosticsBody body)
    {
        await Mediator.Send(new DeviceDiagnosticsCommand(
            CurrentUserId, deviceId,
            body.BatteryLevel, body.LocationPermission,
            body.BackgroundPermission, body.MockLocationDetected));
        return NoContent();
    }
}
