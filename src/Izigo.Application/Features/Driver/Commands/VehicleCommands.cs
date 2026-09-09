using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Driver.Dtos;
using Izigo.Application.Features.Driver.Queries;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Driver.Commands;

// ── POST /driver/vehicles ─────────────────────────────────────────────────────

public record AddVehicleCommand(string DriverId, AddVehicleRequest Request) : IRequest<VehicleDto>;

public class AddVehicleHandler(IApplicationDbContext db)
    : IRequestHandler<AddVehicleCommand, VehicleDto>
{
    public async Task<VehicleDto> Handle(AddVehicleCommand cmd, CancellationToken ct)
    {
        var req = cmd.Request;

        if (!Enum.TryParse<VehicleType>(req.VehicleType, true, out var vt))
            throw new ArgumentException("VALIDATION_ERROR: Invalid vehicle_type.");

        var duplicate = await db.Vehicles
            .AnyAsync(v => v.DriverId == cmd.DriverId && v.Plate == req.Plate, ct);
        if (duplicate)
            throw new InvalidOperationException("CONFLICT: A vehicle with this plate is already registered.");

        var vehicle = new Vehicle
        {
            DriverId      = cmd.DriverId,
            Make          = req.Make,
            Model         = req.Model,
            Year          = req.Year,
            Color         = req.Color,
            Plate         = req.Plate,
            Type          = vt,
            Seats         = req.Seats,
            IsActive      = false,
            PendingReview = true,
        };

        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct);

        return new VehicleDto(vehicle.Id, vehicle.Make, vehicle.Model, vehicle.Year,
            vehicle.Color, vehicle.Plate, vehicle.Type.ToString().ToLower(),
            vehicle.Seats, vehicle.IsActive, vehicle.PendingReview);
    }
}

// ── PATCH /driver/vehicles/{id} ───────────────────────────────────────────────

public record UpdateVehicleCommand(string DriverId, string VehicleId, UpdateVehicleRequest Request)
    : IRequest<VehicleDto>;

public class UpdateVehicleHandler(IApplicationDbContext db)
    : IRequestHandler<UpdateVehicleCommand, VehicleDto>
{
    public async Task<VehicleDto> Handle(UpdateVehicleCommand cmd, CancellationToken ct)
    {
        var vehicle = await db.Vehicles
            .FirstOrDefaultAsync(v => v.Id == cmd.VehicleId && v.DriverId == cmd.DriverId, ct)
            ?? throw new KeyNotFoundException("Vehicle not found.");

        var req = cmd.Request;
        if (req.Color != null) vehicle.Color = req.Color;
        if (req.Plate != null)
        {
            vehicle.Plate         = req.Plate;
            vehicle.PendingReview = true;   // spec: plate change must re-trigger review
            vehicle.IsActive      = false;
        }
        if (req.IsActive != null)
        {
            // Switching active vehicle — deactivate all others
            if (req.IsActive.Value)
            {
                var others = await db.Vehicles
                    .Where(v => v.DriverId == cmd.DriverId && v.Id != cmd.VehicleId)
                    .ToListAsync(ct);
                foreach (var o in others) o.IsActive = false;
            }
            vehicle.IsActive = req.IsActive.Value;
        }

        await db.SaveChangesAsync(ct);

        return new VehicleDto(vehicle.Id, vehicle.Make, vehicle.Model, vehicle.Year,
            vehicle.Color, vehicle.Plate, vehicle.Type.ToString().ToLower(),
            vehicle.Seats, vehicle.IsActive, vehicle.PendingReview);
    }
}

// ── POST /driver/documents ────────────────────────────────────────────────────

public record UploadDocumentCommand(string DriverId, UploadDocumentRequest Request)
    : IRequest<DriverDocumentDto>;

public class UploadDocumentHandler(IApplicationDbContext db)
    : IRequestHandler<UploadDocumentCommand, DriverDocumentDto>
{
    public async Task<DriverDocumentDto> Handle(UploadDocumentCommand cmd, CancellationToken ct)
    {
        var req = cmd.Request;

        if (!Enum.TryParse<DocumentType>(req.Type.Replace("_", ""), true, out var docType))
            throw new ArgumentException("VALIDATION_ERROR: Invalid document type.");

        // Archive any existing doc of same type and insert a fresh one
        var existing = await db.DriverDocuments
            .Where(d => d.DriverId == cmd.DriverId && d.Type == docType)
            .ToListAsync(ct);
        db.DriverDocuments.RemoveRange(existing);

        var doc = new DriverDocument
        {
            DriverId     = cmd.DriverId,
            Type         = docType,
            FileUrl      = req.FileUrl,
            BackFileUrl  = req.BackFileUrl,
            ExpiresAt    = req.ExpiresAt,
            Status       = OnboardingStepStatus.Submitted,
        };

        db.DriverDocuments.Add(doc);
        await db.SaveChangesAsync(ct);

        var daysToExpiry = doc.ExpiresAt.HasValue
            ? (int)(doc.ExpiresAt.Value.Date - DateTime.UtcNow.Date).TotalDays : (int?)null;

        return new DriverDocumentDto(doc.Id, doc.Type.ToString().ToLower(),
            doc.FileUrl, doc.BackFileUrl, doc.Status.ToString().ToLower(),
            doc.RejectionReason, doc.ExpiresAt, daysToExpiry, doc.CreatedAt);
    }
}

// ── PATCH /driver/preferences ─────────────────────────────────────────────────

public record UpdateDriverPreferencesCommand(string DriverId, UpdateDriverPreferencesRequest Request)
    : IRequest<DriverPreferencesDto>;

public class UpdateDriverPreferencesHandler(IApplicationDbContext db)
    : IRequestHandler<UpdateDriverPreferencesCommand, DriverPreferencesDto>
{
    public async Task<DriverPreferencesDto> Handle(UpdateDriverPreferencesCommand cmd, CancellationToken ct)
    {
        var req = cmd.Request;
        var updates = new Dictionary<string, string>();

        if (req.Verticals           != null) updates["driver:verticals"]             = string.Join(",", req.Verticals);
        if (req.MaxPickupDistanceKm != null) updates["driver:max_pickup_km"]         = req.MaxPickupDistanceKm.Value.ToString();
        if (req.AutoAccept          != null) updates["driver:auto_accept"]           = req.AutoAccept.Value.ToString().ToLower();
        if (req.AcceptCash          != null) updates["driver:accept_cash"]           = req.AcceptCash.Value.ToString().ToLower();
        if (req.AcceptWallet        != null) updates["driver:accept_wallet"]         = req.AcceptWallet.Value.ToString().ToLower();
        if (req.AcceptCard          != null) updates["driver:accept_card"]           = req.AcceptCard.Value.ToString().ToLower();
        if (req.AcceptMobileMoney   != null) updates["driver:accept_mobile_money"]   = req.AcceptMobileMoney.Value.ToString().ToLower();
        if (req.NotifyOnNewOffer    != null) updates["driver:notify_on_offer"]       = req.NotifyOnNewOffer.Value.ToString().ToLower();
        if (req.SilentMode          != null) updates["driver:silent_mode"]           = req.SilentMode.Value.ToString().ToLower();

        var existing = await db.UserPreferences
            .Where(p => p.UserId == cmd.DriverId && updates.Keys.Contains(p.Key))
            .ToListAsync(ct);

        foreach (var (key, value) in updates)
        {
            var pref = existing.FirstOrDefault(p => p.Key == key);
            if (pref != null)
                pref.Value = value;
            else
                db.UserPreferences.Add(new UserPreference
                {
                    UserId = cmd.DriverId,
                    Key    = key,
                    Value  = value,
                });
        }

        await db.SaveChangesAsync(ct);
        return await new GetDriverPreferencesHandler(db)
            .Handle(new GetDriverPreferencesQuery(cmd.DriverId), ct);
    }
}
