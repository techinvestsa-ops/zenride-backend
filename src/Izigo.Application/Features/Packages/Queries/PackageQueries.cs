using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Packages.Dtos;
using Izigo.Application.Features.Rides.Helpers;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Packages.Queries;

// ── GET /packages/{id} ───────────────────────────────────────────────────────

public record GetPackageQuery(string SenderId, string PackageId) : IRequest<PackageDto>;

public class GetPackageHandler(IApplicationDbContext db)
    : IRequestHandler<GetPackageQuery, PackageDto>
{
    public async Task<PackageDto> Handle(GetPackageQuery req, CancellationToken ct)
    {
        var pkg = await db.Packages
            .FirstOrDefaultAsync(p => p.Id == req.PackageId && p.SenderId == req.SenderId, ct)
            ?? throw new KeyNotFoundException("Package not found.");

        return await PackageMapper.BuildAsync(pkg, db, ct);
    }
}

// ── GET /packages — history ───────────────────────────────────────────────────

public record GetPackagesQuery(
    string SenderId,
    string? Status,
    int Page,
    int PerPage
) : IRequest<(List<PackageHistoryItemDto> Items, int Total)>;

public class GetPackagesHandler(IApplicationDbContext db)
    : IRequestHandler<GetPackagesQuery, (List<PackageHistoryItemDto>, int)>
{
    public async Task<(List<PackageHistoryItemDto>, int)> Handle(
        GetPackagesQuery req, CancellationToken ct)
    {
        var q = db.Packages.Where(p => p.SenderId == req.SenderId);

        if (!string.IsNullOrEmpty(req.Status) &&
            Enum.TryParse<PackageStatus>(req.Status, true, out var status))
            q = q.Where(p => p.Status == status);

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(p => p.CreatedAt)
            .Skip((req.Page - 1) * req.PerPage)
            .Take(req.PerPage)
            .Select(p => new PackageHistoryItemDto(
                p.Id, p.TrackingId, p.Status.ToString().ToLower(),
                p.PickupLabel, p.DropoffLabel,
                p.RecipientName, p.FareTotal, p.Currency,
                p.DeliveredAt, p.CreatedAt))
            .ToListAsync(ct);

        return (items, total);
    }
}

// ── GET /packages/track/{trackingId} — public, no auth ───────────────────────

public record TrackPackageQuery(string TrackingId) : IRequest<PackageTrackDto>;

public class TrackPackageHandler(IApplicationDbContext db)
    : IRequestHandler<TrackPackageQuery, PackageTrackDto>
{
    public async Task<PackageTrackDto> Handle(TrackPackageQuery req, CancellationToken ct)
    {
        var pkg = await db.Packages
            .FirstOrDefaultAsync(p => p.TrackingId == req.TrackingId, ct)
            ?? throw new KeyNotFoundException("Package not found.");

        return new PackageTrackDto(
            TrackingId: pkg.TrackingId,
            Status: pkg.Status.ToString().ToLower(),
            Pickup: new PackageLocationDto(
                pkg.PickupLabel, (double)pkg.PickupLat, (double)pkg.PickupLng),
            Dropoff: new PackageLocationDto(
                pkg.DropoffLabel, (double)pkg.DropoffLat, (double)pkg.DropoffLng),
            RecipientName: pkg.RecipientName,
            EstimatedDelivery: null,   // requires route ETA — wired when geo service is live
            PickedUpAt: pkg.PickedUpAt,
            DeliveredAt: pkg.DeliveredAt);
    }
}

// ── Internal mapper ───────────────────────────────────────────────────────────

internal static class PackageMapper
{
    public static async Task<PackageDto> BuildAsync(
        Domain.Entities.Package pkg, IApplicationDbContext db, CancellationToken ct)
    {
        PackageCourierDto? courier = null;
        if (pkg.CourierId != null)
        {
            var dp = await db.DriverProfiles
                .Include(d => d.User)
                .Include(d => d.Vehicles.Where(v => v.IsActive))
                .FirstOrDefaultAsync(d => d.UserId == pkg.CourierId, ct);

            if (dp != null)
            {
                var v = dp.Vehicles.FirstOrDefault();
                courier = new PackageCourierDto(
                    Id: dp.UserId,
                    Name: $"{dp.User.FirstName} {dp.User.LastName[..1]}.",
                    Rating: (double)dp.User.Rating,
                    PhotoUrl: dp.User.PhotoUrl,
                    PhoneMasked: PhoneMasker.Mask(dp.User.Phone),
                    Vehicle: v == null ? null : new PackageVehicleDto(
                        $"{v.Make} {v.Model}".Trim(), v.Plate, v.Color));
            }
        }

        var isFinal = pkg.Status is PackageStatus.Delivered or
                      PackageStatus.Cancelled or PackageStatus.Returned;

        return new PackageDto(
            PackageId: pkg.Id,
            TrackingId: pkg.TrackingId,
            Status: pkg.Status.ToString().ToLower(),
            Pickup: new PackageLocationDto(
                pkg.PickupLabel, (double)pkg.PickupLat, (double)pkg.PickupLng),
            Dropoff: new PackageLocationDto(
                pkg.DropoffLabel, (double)pkg.DropoffLat, (double)pkg.DropoffLng),
            Recipient: new PackageRecipientDto(pkg.RecipientName,
                PhoneMasker.Mask(pkg.RecipientPhone)),
            Size: pkg.Size,
            IsExpress: pkg.IsExpress,
            IsFragile: pkg.IsFragile,
            Description: pkg.Description,
            RecipientPays: pkg.RecipientPays,
            FareTotal: pkg.FareTotal,
            Currency: pkg.Currency,
            PaymentMethod: pkg.PaymentMethod.ToString().ToLower(),
            Courier: courier,
            Actions: new PackageActionsDto(
                CanCancel: !isFinal && pkg.Status != PackageStatus.PickedUp,
                CanRate: pkg.Status == PackageStatus.Delivered),
            CreatedAt: pkg.CreatedAt,
            PickedUpAt: pkg.PickedUpAt,
            DeliveredAt: pkg.DeliveredAt,
            CancelledAt: pkg.CancelledAt);
    }
}
