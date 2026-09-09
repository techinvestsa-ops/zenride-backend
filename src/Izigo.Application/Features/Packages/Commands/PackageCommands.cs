using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Packages.Dtos;
using Izigo.Application.Features.Packages.Queries;
using Izigo.Application.Features.Quotes.Dtos;
using Izigo.Application.Features.Rides.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Packages.Commands;

// ── POST /packages ────────────────────────────────────────────────────────────

public record CreatePackageCommand(string SenderId, CreatePackageRequest Request, string? IdempotencyKey)
    : IRequest<PackageDto>;

public class CreatePackageHandler(IApplicationDbContext db, IIdempotencyService idempotency)
    : IRequestHandler<CreatePackageCommand, PackageDto>
{
    private static readonly System.Text.Json.JsonSerializerOptions _jsonOpts = new()
        { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower };

    public async Task<PackageDto> Handle(CreatePackageCommand cmd, CancellationToken ct)
    {
        var req = cmd.Request;

        // Idempotency cache check
        if (cmd.IdempotencyKey != null)
        {
            var cached = await idempotency.GetAsync(cmd.IdempotencyKey, cmd.SenderId, ct);
            if (cached != null)
            {
                var cachedDto = System.Text.Json.JsonSerializer.Deserialize<PackageDto>(
                    cached.ResponseJson, _jsonOpts);
                if (cachedDto != null) return cachedDto;
            }
        }

        if (!Enum.TryParse<PaymentMethod>(req.PaymentMethod.Replace("_", ""), true, out var payMethod))
            throw new ArgumentException("VALIDATION_ERROR: Invalid payment_method.");

        if (!Enum.TryParse<ServiceClass>(req.ClassCode.Replace("_", ""), true, out var serviceClass) ||
            serviceClass is not (ServiceClass.PackageSmall or ServiceClass.PackageLarge))
            throw new ArgumentException("VALIDATION_ERROR: class_code must be package_small or package_large.");

        var quote = await db.Quotes
            .FirstOrDefaultAsync(q => q.Id == req.QuoteId && q.RiderId == cmd.SenderId, ct)
            ?? throw new KeyNotFoundException("Quote not found.");

        if (quote.IsUsed)
        {
            var existingPkg = await db.Packages.FirstOrDefaultAsync(p => p.QuoteId == req.QuoteId, ct);
            if (existingPkg != null)
                return await PackageMapper.BuildAsync(existingPkg, db, ct);
            throw new InvalidOperationException("CONFLICT: Quote already used.");
        }

        if (quote.ExpiresAt <= DateTime.UtcNow)
            throw new InvalidOperationException("CONFLICT: Quote has expired.");

        if (quote.Vertical != Vertical.Package)
            throw new ArgumentException("VALIDATION_ERROR: Quote is not for a package delivery.");

        var options = System.Text.Json.JsonSerializer
            .Deserialize<QuoteOptionDto[]>(quote.OptionsJson) ?? [];

        var classCodeStr = serviceClass == ServiceClass.PackageSmall ? "package_small" : "package_large";
        var option = options.FirstOrDefault(o =>
            string.Equals(o.ClassCode, classCodeStr, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"VALIDATION_ERROR: class_code '{req.ClassCode}' not in this quote.");

        quote.IsUsed = true;

        var pkg = new Package
        {
            TrackingId      = TripCode.GenerateDelivery(),
            SenderId        = cmd.SenderId,
            QuoteId         = quote.Id,
            PickupLat       = quote.PickupLat,
            PickupLng       = quote.PickupLng,
            PickupLabel     = quote.PickupLabel,
            DropoffLat      = quote.DropoffLat,
            DropoffLng      = quote.DropoffLng,
            DropoffLabel    = quote.DropoffLabel,
            RecipientName   = req.RecipientName,
            RecipientPhone  = req.RecipientPhone,
            Size            = serviceClass == ServiceClass.PackageSmall ? "small" : "large",
            IsExpress       = req.IsExpress,
            IsFragile       = req.IsFragile,
            Description     = req.Description,
            RecipientPays   = req.RecipientPays,
            DeclaredValue   = req.DeclaredValue,
            FareTotal       = option.Fare.Total,
            Currency        = quote.Currency,
            PaymentMethod   = payMethod,
            Status          = PackageStatus.Searching,
            Market          = "ci",
        };

        db.Packages.Add(pkg);
        await db.SaveChangesAsync(ct);

        var result = await PackageMapper.BuildAsync(pkg, db, ct);

        if (cmd.IdempotencyKey != null)
            await idempotency.SaveAsync(cmd.IdempotencyKey, cmd.SenderId, "POST /packages",
                System.Text.Json.JsonSerializer.Serialize(result, _jsonOpts), 200, ct);

        return result;
    }
}

// ── POST /packages/{id}/cancel ────────────────────────────────────────────────

public record CancelPackageCommand(string SenderId, string PackageId, string? Reason) : IRequest;

public class CancelPackageHandler(IApplicationDbContext db)
    : IRequestHandler<CancelPackageCommand>
{
    public async Task Handle(CancelPackageCommand cmd, CancellationToken ct)
    {
        var pkg = await db.Packages
            .FirstOrDefaultAsync(p => p.Id == cmd.PackageId && p.SenderId == cmd.SenderId, ct)
            ?? throw new KeyNotFoundException("Package not found.");

        if (pkg.Status is PackageStatus.Delivered or
            PackageStatus.Cancelled or PackageStatus.Returned)
            throw new InvalidOperationException("CONFLICT: Package is already finalised.");

        if (pkg.Status == PackageStatus.PickedUp)
            throw new InvalidOperationException("CONFLICT: Package is in transit and cannot be cancelled.");

        pkg.Status = PackageStatus.Cancelled;
        pkg.CancelledAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}

// ── POST /packages/{id}/rate ──────────────────────────────────────────────────

public record RatePackageCommand(string SenderId, string PackageId, RatePackageRequest Request)
    : IRequest;

public class RatePackageHandler(IApplicationDbContext db)
    : IRequestHandler<RatePackageCommand>
{
    public async Task Handle(RatePackageCommand cmd, CancellationToken ct)
    {
        var pkg = await db.Packages
            .FirstOrDefaultAsync(p => p.Id == cmd.PackageId && p.SenderId == cmd.SenderId, ct)
            ?? throw new KeyNotFoundException("Package not found.");

        if (pkg.Status != PackageStatus.Delivered)
            throw new InvalidOperationException("CONFLICT: Can only rate delivered packages.");

        if (pkg.CourierId != null)
        {
            var dp = await db.DriverProfiles
                .Include(d => d.User)
                .FirstOrDefaultAsync(d => d.UserId == pkg.CourierId, ct);
            if (dp != null)
            {
                var total = dp.User.TotalRatings;
                dp.User.Rating = ((dp.User.Rating * total) + cmd.Request.Stars) / (total + 1);
                dp.User.TotalRatings++;
            }
        }

        await db.SaveChangesAsync(ct);
    }
}

// ── POST /driver/packages/{id}/proof — upload proof of delivery ───────────────

public record UploadProofCommand(string CourierId, string PackageId, string? ProofCode, string? PhotoUrl)
    : IRequest<ProofOfDeliveryDto>;

public class UploadProofHandler(IApplicationDbContext db)
    : IRequestHandler<UploadProofCommand, ProofOfDeliveryDto>
{
    public async Task<ProofOfDeliveryDto> Handle(UploadProofCommand cmd, CancellationToken ct)
    {
        var pkg = await db.Packages
            .FirstOrDefaultAsync(p => p.Id == cmd.PackageId && p.CourierId == cmd.CourierId, ct)
            ?? throw new KeyNotFoundException("Package not found.");

        if (pkg.Status == PackageStatus.Delivered)
            return new ProofOfDeliveryDto(pkg.ProofCode!, pkg.ProofPhotoUrl);

        pkg.ProofCode     = cmd.ProofCode ?? Guid.CreateVersion7().ToString("N")[..6].ToUpper();
        pkg.ProofPhotoUrl = cmd.PhotoUrl;
        pkg.Status        = PackageStatus.Delivered;
        pkg.DeliveredAt   = DateTime.UtcNow;

        if (pkg.CourierId != null)
        {
            var dp = await db.DriverProfiles
                .FirstOrDefaultAsync(d => d.UserId == pkg.CourierId, ct);
            if (dp != null)
                dp.TotalTripsCompleted++;
        }

        await db.SaveChangesAsync(ct);
        return new ProofOfDeliveryDto(pkg.ProofCode!, pkg.ProofPhotoUrl);
    }
}

// ── POST /driver/packages/{id}/failed-delivery ────────────────────────────────

public record ReportFailedDeliveryCommand(string CourierId, string PackageId, string Reason)
    : IRequest;

public class ReportFailedDeliveryHandler(IApplicationDbContext db)
    : IRequestHandler<ReportFailedDeliveryCommand>
{
    public async Task Handle(ReportFailedDeliveryCommand cmd, CancellationToken ct)
    {
        var pkg = await db.Packages
            .FirstOrDefaultAsync(p => p.Id == cmd.PackageId && p.CourierId == cmd.CourierId, ct)
            ?? throw new KeyNotFoundException("Package not found.");

        if (pkg.Status == PackageStatus.Delivered)
            throw new InvalidOperationException("CONFLICT: Package is already delivered.");

        pkg.FailedDeliveryReason = cmd.Reason;
        pkg.Status = PackageStatus.Returned;
        pkg.CancelledAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
