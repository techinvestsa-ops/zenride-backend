using Izigo.Application.Common.Interfaces;
using Izigo.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Devices.Commands;

// ── POST /devices — upsert on device_id ──────────────────────────────────────

public record RegisterDeviceCommand(
    string UserId,
    string FcmToken,
    string DeviceId,
    string Platform,
    string? AppVersion,
    string? OsVersion,
    string? Model,
    string? Locale,
    string? Timezone
) : IRequest;

public class RegisterDeviceHandler(IApplicationDbContext db)
    : IRequestHandler<RegisterDeviceCommand>
{
    public async Task Handle(RegisterDeviceCommand req, CancellationToken ct)
    {
        var existing = await db.UserDevices
            .FirstOrDefaultAsync(d => d.UserId == req.UserId && d.DeviceId == req.DeviceId, ct);

        if (existing == null)
        {
            db.UserDevices.Add(new UserDevice
            {
                UserId    = req.UserId,
                DeviceId  = req.DeviceId,
                FcmToken  = req.FcmToken,
                Platform  = req.Platform,
                AppVersion = req.AppVersion,
                OsVersion  = req.OsVersion,
                Model      = req.Model,
                Locale     = req.Locale,
                Timezone   = req.Timezone,
                LastActiveAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.FcmToken   = req.FcmToken;
            existing.Platform   = req.Platform;
            existing.AppVersion = req.AppVersion ?? existing.AppVersion;
            existing.OsVersion  = req.OsVersion  ?? existing.OsVersion;
            existing.Model      = req.Model       ?? existing.Model;
            existing.Locale     = req.Locale      ?? existing.Locale;
            existing.Timezone   = req.Timezone    ?? existing.Timezone;
            existing.LastActiveAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }
}

// ── DELETE /devices/{device_id} ───────────────────────────────────────────────

public record UnregisterDeviceCommand(string UserId, string DeviceId) : IRequest;

public class UnregisterDeviceHandler(IApplicationDbContext db)
    : IRequestHandler<UnregisterDeviceCommand>
{
    public async Task Handle(UnregisterDeviceCommand req, CancellationToken ct)
    {
        var device = await db.UserDevices
            .FirstOrDefaultAsync(d => d.UserId == req.UserId && d.DeviceId == req.DeviceId, ct);

        if (device != null)
        {
            device.FcmToken = null;   // stop push; don't delete for session history
            await db.SaveChangesAsync(ct);
        }
    }
}

// ── POST /devices/{device_id}/diagnostics — driver only ──────────────────────

public record DeviceDiagnosticsCommand(
    string UserId,
    string DeviceId,
    int? BatteryLevel,
    bool? LocationPermission,
    bool? BackgroundPermission,
    bool? MockLocationDetected
) : IRequest;

public class DeviceDiagnosticsHandler(IApplicationDbContext db)
    : IRequestHandler<DeviceDiagnosticsCommand>
{
    public async Task Handle(DeviceDiagnosticsCommand req, CancellationToken ct)
    {
        db.DeviceDiagnostics.Add(new DeviceDiagnostic
        {
            UserId                = req.UserId,
            DeviceId              = req.DeviceId,
            BatteryLevel          = req.BatteryLevel,
            LocationPermission    = req.LocationPermission,
            BackgroundPermission  = req.BackgroundPermission,
            MockLocationDetected  = req.MockLocationDetected
        });

        await db.SaveChangesAsync(ct);
    }
}
