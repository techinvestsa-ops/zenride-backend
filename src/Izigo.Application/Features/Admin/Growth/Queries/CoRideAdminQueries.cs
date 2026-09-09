using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Models;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Admin.Growth.Queries;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record CoRideListingRowDto(
    string Id, string DriverName, string DriverId,
    string Route, DateTime DepartureAt,
    int SeatsTaken, int SeatsTotal, long PricePerSeat,
    bool IsEco, string Status, string Currency, long Gross);

public record CoRidePassengerDto(string BookingId, string RiderId, string RiderName,
    string SeatLabels, string PaymentStatus, bool Boarded);

public record CoRideListingDetailDto(CoRideListingRowDto Listing, IEnumerable<CoRidePassengerDto> Manifest);

public record CoRideRequestRowDto(string Id, string RiderId, string RiderName,
    string PickupLabel, string DropoffLabel, int SeatsNeeded,
    DateTime DepartureWindowFrom, DateTime DepartureWindowTo, string Status);

public record PackageRowDto(string Id, string TrackingId, string SenderId, string SenderName,
    string RecipientName, string? CourierId, string? CourierName,
    string Size, bool IsFragile, bool HasProof, long FareTotal, string Currency, string Status);

public record PackageDetailDto(string Id, string TrackingId, string SenderId, string SenderName,
    string RecipientName, string RecipientPhone, string? CourierId, string? CourierName,
    string Size, bool IsExpress, bool IsFragile, string? Description,
    string? ProofPhotoUrl, string? ProofCode, long? DeclaredValue,
    long FareTotal, string Currency, string Status,
    DateTime? PickedUpAt, DateTime? DeliveredAt, DateTime? CancelledAt);

// ── GET /admin/co-ride/listings ───────────────────────────────────────────────

public record GetCoRideListingsQuery(string? Status, string? Zone, string? DriverId,
    int Page, int PerPage) : IRequest<object>;

public class GetCoRideListingsHandler(IApplicationDbContext db)
    : IRequestHandler<GetCoRideListingsQuery, object>
{
    public async Task<object> Handle(GetCoRideListingsQuery req, CancellationToken ct)
    {
        var query = db.CoRideListings.AsQueryable();

        if (!string.IsNullOrWhiteSpace(req.Status))
            query = query.Where(l => l.Status == req.Status.ToLower());

        if (!string.IsNullOrWhiteSpace(req.DriverId))
            query = query.Where(l => l.DriverId == req.DriverId);

        var perPage  = Math.Max(1, Math.Min(100, req.PerPage));
        var total    = await query.CountAsync(ct);
        var lastPage = (int)Math.Ceiling((double)total / perPage);

        var listings = await query
            .OrderByDescending(l => l.DepartureAt)
            .Skip((req.Page - 1) * perPage).Take(perPage)
            .Select(l => new { l.Id, l.DriverId, l.FromLabel, l.ToLabel, l.DepartureAt,
                               l.SeatsTaken, l.SeatsTotal, l.PricePerSeat, l.IsEco, l.Status, l.Currency })
            .ToListAsync(ct);

        var driverIds = listings.Select(l => l.DriverId).Distinct().ToList();
        var drivers   = await db.DriverProfiles
            .Where(d => driverIds.Contains(d.Id))
            .Select(d => new { d.Id, d.User.FirstName, d.User.LastName })
            .ToDictionaryAsync(d => d.Id, ct);

        var rows = listings.Select(l =>
        {
            drivers.TryGetValue(l.DriverId, out var drv);
            var gross = (long)l.SeatsTaken * l.PricePerSeat;
            return new CoRideListingRowDto(
                l.Id,
                drv is not null ? $"{drv.FirstName} {drv.LastName}".Trim() : "—",
                l.DriverId,
                $"{l.FromLabel} → {l.ToLabel}",
                l.DepartureAt, l.SeatsTaken, l.SeatsTotal,
                l.PricePerSeat, l.IsEco, l.Status, l.Currency, gross);
        }).ToArray();

        var facets = new Dictionary<string, Dictionary<string, int>>
        {
            ["status"] = listings.GroupBy(l => l.Status)
                .ToDictionary(g => g.Key, g => g.Count())
        };

        return AdminApiResponse.Ok(rows, new AdminPagedMeta(req.Page, perPage, total, lastPage, facets));
    }
}

// ── GET /admin/co-ride/listings/{id} ──────────────────────────────────────────

public record GetCoRideListingDetailQuery(string ListingId) : IRequest<CoRideListingDetailDto?>;

public class GetCoRideListingDetailHandler(IApplicationDbContext db)
    : IRequestHandler<GetCoRideListingDetailQuery, CoRideListingDetailDto?>
{
    public async Task<CoRideListingDetailDto?> Handle(GetCoRideListingDetailQuery req, CancellationToken ct)
    {
        var listing = await db.CoRideListings
            .Where(l => l.Id == req.ListingId)
            .Select(l => new { l.Id, l.DriverId, l.FromLabel, l.ToLabel, l.DepartureAt,
                               l.SeatsTaken, l.SeatsTotal, l.PricePerSeat, l.IsEco, l.Status, l.Currency })
            .FirstOrDefaultAsync(ct);

        if (listing is null) return null;

        var driver = await db.DriverProfiles
            .Where(d => d.Id == listing.DriverId)
            .Select(d => new { d.User.FirstName, d.User.LastName })
            .FirstOrDefaultAsync(ct);

        var gross = (long)listing.SeatsTaken * listing.PricePerSeat;
        var listingRow = new CoRideListingRowDto(
            listing.Id,
            driver is not null ? $"{driver.FirstName} {driver.LastName}".Trim() : "—",
            listing.DriverId,
            $"{listing.FromLabel} → {listing.ToLabel}",
            listing.DepartureAt, listing.SeatsTaken, listing.SeatsTotal,
            listing.PricePerSeat, listing.IsEco, listing.Status, listing.Currency, gross);

        var bookings = await db.CoRideBookings
            .Where(b => b.ListingId == req.ListingId)
            .Select(b => new { b.Id, b.RiderId, b.SeatLabelsJson, b.Status,
                               b.PaymentMethod, b.Total })
            .ToListAsync(ct);

        var riderIds = bookings.Select(b => b.RiderId).Distinct().ToList();
        var riders   = await db.Users
            .Where(u => riderIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName })
            .ToDictionaryAsync(u => u.Id, ct);

        var manifest = bookings.Select(b =>
        {
            riders.TryGetValue(b.RiderId, out var rider);
            return new CoRidePassengerDto(
                b.Id,
                b.RiderId,
                rider is not null ? $"{rider.FirstName} {rider.LastName}".Trim() : "—",
                b.SeatLabelsJson,
                b.Status.ToString().ToLower(),
                b.Status == CoRideBookingStatus.InRide || b.Status == CoRideBookingStatus.Completed);
        }).ToArray();

        return new CoRideListingDetailDto(listingRow, manifest);
    }
}

// ── GET /admin/co-ride/requests ───────────────────────────────────────────────

public record GetCoRideRequestsQuery(int Page, int PerPage) : IRequest<object>;

public class GetCoRideRequestsHandler(IApplicationDbContext db)
    : IRequestHandler<GetCoRideRequestsQuery, object>
{
    public async Task<object> Handle(GetCoRideRequestsQuery req, CancellationToken ct)
    {
        var perPage = Math.Max(1, Math.Min(100, req.PerPage));
        var query   = db.CoRideRequests.Where(r => r.Status == CoRideRequestStatus.Searching);
        var total   = await query.CountAsync(ct);
        var lastPage = (int)Math.Ceiling((double)total / perPage);

        var requests = await query
            .OrderBy(r => r.CreatedAt)
            .Skip((req.Page - 1) * perPage).Take(perPage)
            .Select(r => new { r.Id, r.RiderId, r.PickupLabel, r.DropoffLabel,
                               r.SeatsNeeded, r.DepartureWindowFrom, r.DepartureWindowTo, r.Status })
            .ToListAsync(ct);

        var riderIds = requests.Select(r => r.RiderId).Distinct().ToList();
        var riders   = await db.Users
            .Where(u => riderIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName })
            .ToDictionaryAsync(u => u.Id, ct);

        var rows = requests.Select(r =>
        {
            riders.TryGetValue(r.RiderId, out var rider);
            return new CoRideRequestRowDto(
                r.Id, r.RiderId,
                rider is not null ? $"{rider.FirstName} {rider.LastName}".Trim() : "—",
                r.PickupLabel, r.DropoffLabel, r.SeatsNeeded,
                r.DepartureWindowFrom, r.DepartureWindowTo,
                r.Status.ToString().ToLower());
        }).ToArray();

        return AdminApiResponse.Ok(rows, new AdminPagedMeta(req.Page, perPage, total, lastPage));
    }
}

// ── GET /admin/packages ───────────────────────────────────────────────────────

public record GetAdminPackagesQuery(string Market, string? Status, bool? Proof,
    string? Size, bool? Fragile, string? CourierId, int Page, int PerPage) : IRequest<object>;

public class GetAdminPackagesHandler(IApplicationDbContext db)
    : IRequestHandler<GetAdminPackagesQuery, object>
{
    public async Task<object> Handle(GetAdminPackagesQuery req, CancellationToken ct)
    {
        var query = db.Packages.Where(p => p.Market == req.Market).AsQueryable();

        if (!string.IsNullOrWhiteSpace(req.Status) &&
            Enum.TryParse<PackageStatus>(req.Status, true, out var status))
            query = query.Where(p => p.Status == status);

        if (req.Proof == true)  query = query.Where(p => p.ProofPhotoUrl != null);
        if (req.Proof == false) query = query.Where(p => p.ProofPhotoUrl == null);
        if (!string.IsNullOrWhiteSpace(req.Size))      query = query.Where(p => p.Size == req.Size);
        if (req.Fragile.HasValue) query = query.Where(p => p.IsFragile == req.Fragile.Value);
        if (!string.IsNullOrWhiteSpace(req.CourierId)) query = query.Where(p => p.CourierId == req.CourierId);

        var perPage  = Math.Max(1, Math.Min(100, req.PerPage));
        var total    = await query.CountAsync(ct);
        var lastPage = (int)Math.Ceiling((double)total / perPage);

        var packages = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((req.Page - 1) * perPage).Take(perPage)
            .Select(p => new { p.Id, p.TrackingId, p.SenderId, p.CourierId, p.RecipientName,
                               p.Size, p.IsFragile, p.ProofPhotoUrl, p.FareTotal, p.Currency, p.Status })
            .ToListAsync(ct);

        var userIds   = packages.Select(p => p.SenderId).Distinct().ToList();
        var courierIds = packages.Select(p => p.CourierId).Where(c => c != null).Distinct().ToList();

        var senders = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName })
            .ToDictionaryAsync(u => u.Id, ct);

        var couriers = await db.DriverProfiles
            .Where(d => courierIds.Contains(d.Id))
            .Select(d => new { d.Id, d.User.FirstName, d.User.LastName })
            .ToDictionaryAsync(d => d.Id, ct);

        var rows = packages.Select(p =>
        {
            senders.TryGetValue(p.SenderId, out var sender);
            couriers.TryGetValue(p.CourierId ?? "", out var courier);
            return new PackageRowDto(
                p.Id, p.TrackingId, p.SenderId,
                sender is not null ? $"{sender.FirstName} {sender.LastName}".Trim() : "—",
                p.RecipientName,
                p.CourierId, courier is not null ? $"{courier.FirstName} {courier.LastName}".Trim() : null,
                p.Size, p.IsFragile, p.ProofPhotoUrl != null,
                p.FareTotal, p.Currency, p.Status.ToString().ToLower());
        }).ToArray();

        var facets = new Dictionary<string, Dictionary<string, int>>
        {
            ["status"] = packages.GroupBy(p => p.Status.ToString())
                .ToDictionary(g => g.Key.ToLower(), g => g.Count())
        };

        return AdminApiResponse.Ok(rows, new AdminPagedMeta(req.Page, perPage, total, lastPage, facets));
    }
}

// ── GET /admin/packages/{id} ──────────────────────────────────────────────────

public record GetAdminPackageDetailQuery(string PackageId) : IRequest<PackageDetailDto?>;

public class GetAdminPackageDetailHandler(IApplicationDbContext db)
    : IRequestHandler<GetAdminPackageDetailQuery, PackageDetailDto?>
{
    public async Task<PackageDetailDto?> Handle(GetAdminPackageDetailQuery req, CancellationToken ct)
    {
        var pkg = await db.Packages.FirstOrDefaultAsync(p => p.Id == req.PackageId, ct);
        if (pkg is null) return null;

        var sender = await db.Users
            .Where(u => u.Id == pkg.SenderId)
            .Select(u => new { u.FirstName, u.LastName })
            .FirstOrDefaultAsync(ct);

        string? courierName = null;
        if (pkg.CourierId is not null)
        {
            var courier = await db.DriverProfiles
                .Where(d => d.Id == pkg.CourierId)
                .Select(d => new { d.User.FirstName, d.User.LastName })
                .FirstOrDefaultAsync(ct);
            courierName = courier is not null ? $"{courier.FirstName} {courier.LastName}".Trim() : null;
        }

        return new PackageDetailDto(
            pkg.Id, pkg.TrackingId, pkg.SenderId,
            sender is not null ? $"{sender.FirstName} {sender.LastName}".Trim() : "—",
            pkg.RecipientName, pkg.RecipientPhone,
            pkg.CourierId, courierName,
            pkg.Size, pkg.IsExpress, pkg.IsFragile, pkg.Description,
            pkg.ProofPhotoUrl, pkg.ProofCode, pkg.DeclaredValue,
            pkg.FareTotal, pkg.Currency, pkg.Status.ToString().ToLower(),
            pkg.PickedUpAt, pkg.DeliveredAt, pkg.CancelledAt);
    }
}
