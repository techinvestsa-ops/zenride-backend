using FluentAssertions;
using Izigo.Application.Features.Packages.Commands;
using Izigo.Application.Features.Packages.Dtos;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Enums;
using Xunit;
using PackageEntity = Izigo.Domain.Entities.Package;
using UserEntity = Izigo.Domain.Entities.User;
using DriverProfileEntity = Izigo.Domain.Entities.DriverProfile;

namespace Izigo.Application.Tests.Features.Packages;

public class PackageHandlerTests
{
    private static PackageEntity BuildPackage(string senderId, string? courierId = null,
        PackageStatus status = PackageStatus.Searching) => new()
    {
        TrackingId     = "PKG-001",
        SenderId       = senderId,
        CourierId      = courierId,
        QuoteId        = Guid.NewGuid().ToString(),
        PickupLat      = 5.3m, PickupLng  = -4.0m, PickupLabel  = "Pickup",
        DropoffLat     = 5.4m, DropoffLng = -4.1m, DropoffLabel = "Dropoff",
        RecipientName  = "John Doe",
        RecipientPhone = "+2250700000001",
        Size           = "small",
        Status         = status,
        FareTotal      = 1500,
        Currency       = "XOF",
        PaymentMethod  = PaymentMethod.Cash,
        Market         = "ci"
    };

    // ── CancelPackageHandler ──────────────────────────────────────────────────

    [Fact]
    public async Task CancelPackage_NotFound_ThrowsKeyNotFound()
    {
        using var db = DbContextFactory.Create();
        var handler = new CancelPackageHandler(db);

        var act = () => handler.Handle(
            new CancelPackageCommand("sender1", "nonexistent-pkg", null), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Theory]
    [InlineData(PackageStatus.Delivered)]
    [InlineData(PackageStatus.Cancelled)]
    [InlineData(PackageStatus.Returned)]
    public async Task CancelPackage_TerminalStatus_ThrowsConflict(PackageStatus status)
    {
        using var db = DbContextFactory.Create();
        var senderId = Guid.NewGuid().ToString();
        var pkg = BuildPackage(senderId, status: status);
        db.Packages.Add(pkg);
        await db.SaveChangesAsync();

        var handler = new CancelPackageHandler(db);
        var act = () => handler.Handle(
            new CancelPackageCommand(senderId, pkg.Id, null), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*finalised*");
    }

    [Fact]
    public async Task CancelPackage_PickedUp_ThrowsConflict()
    {
        using var db = DbContextFactory.Create();
        var senderId = Guid.NewGuid().ToString();
        var pkg = BuildPackage(senderId, status: PackageStatus.PickedUp);
        db.Packages.Add(pkg);
        await db.SaveChangesAsync();

        var handler = new CancelPackageHandler(db);
        var act = () => handler.Handle(
            new CancelPackageCommand(senderId, pkg.Id, null), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*in transit*");
    }

    [Theory]
    [InlineData(PackageStatus.Searching)]
    [InlineData(PackageStatus.Matched)]
    public async Task CancelPackage_CancellableStatus_SetsCancelled(PackageStatus status)
    {
        using var db = DbContextFactory.Create();
        var senderId = Guid.NewGuid().ToString();
        var pkg = BuildPackage(senderId, status: status);
        db.Packages.Add(pkg);
        await db.SaveChangesAsync();

        var handler = new CancelPackageHandler(db);
        await handler.Handle(
            new CancelPackageCommand(senderId, pkg.Id, "changed mind"), CancellationToken.None);

        db.Packages.Find(pkg.Id)!.Status.Should().Be(PackageStatus.Cancelled);
    }

    // ── RatePackageHandler ────────────────────────────────────────────────────

    [Fact]
    public async Task RatePackage_NotFound_ThrowsKeyNotFound()
    {
        using var db = DbContextFactory.Create();
        var handler = new RatePackageHandler(db);

        var act = () => handler.Handle(
            new RatePackageCommand("sender1", "nonexistent", new RatePackageRequest(5, null)),
            CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task RatePackage_NotDelivered_ThrowsConflict()
    {
        using var db = DbContextFactory.Create();
        var senderId = Guid.NewGuid().ToString();
        var pkg = BuildPackage(senderId, status: PackageStatus.Searching);
        db.Packages.Add(pkg);
        await db.SaveChangesAsync();

        var handler = new RatePackageHandler(db);
        var act = () => handler.Handle(
            new RatePackageCommand(senderId, pkg.Id, new RatePackageRequest(5, null)),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*delivered*");
    }

    [Fact]
    public async Task RatePackage_Delivered_NoCourier_Succeeds()
    {
        using var db = DbContextFactory.Create();
        var senderId = Guid.NewGuid().ToString();
        var pkg = BuildPackage(senderId, status: PackageStatus.Delivered);
        db.Packages.Add(pkg);
        await db.SaveChangesAsync();

        var handler = new RatePackageHandler(db);
        await handler.Handle(
            new RatePackageCommand(senderId, pkg.Id, new RatePackageRequest(5, "Great!")),
            CancellationToken.None);

        // No exception; courier is null so no rating update needed
    }

    [Fact]
    public async Task RatePackage_Delivered_WithCourier_UpdatesCourierRating()
    {
        using var db = DbContextFactory.Create();
        var senderId  = Guid.NewGuid().ToString();
        var courierId = Guid.NewGuid().ToString();

        var courierUser = new UserEntity { FirstName = "Driver", LastName = "Test", Phone = "+2250799990001", Role = UserRole.Driver, Rating = 4.0m, TotalRatings = 5 };
        db.Users.Add(courierUser);
        var dp = new DriverProfileEntity { UserId = courierUser.Id };
        db.DriverProfiles.Add(dp);

        var pkg = BuildPackage(senderId, courierId: courierUser.Id, status: PackageStatus.Delivered);
        db.Packages.Add(pkg);
        await db.SaveChangesAsync();

        var handler = new RatePackageHandler(db);
        await handler.Handle(
            new RatePackageCommand(senderId, pkg.Id, new RatePackageRequest(5, null)),
            CancellationToken.None);

        var updatedUser = db.Users.Find(courierUser.Id)!;
        updatedUser.TotalRatings.Should().Be(6);
        updatedUser.Rating.Should().BeApproximately((4.0m * 5 + 5) / 6, 0.001m);
    }

    // ── UploadProofHandler ────────────────────────────────────────────────────

    [Fact]
    public async Task UploadProof_NotFound_ThrowsKeyNotFound()
    {
        using var db = DbContextFactory.Create();
        var handler = new UploadProofHandler(db);

        var act = () => handler.Handle(
            new UploadProofCommand("courier1", "nonexistent", null, null), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task UploadProof_AlreadyDelivered_ReturnsExistingProof()
    {
        using var db = DbContextFactory.Create();
        var courierId = Guid.NewGuid().ToString();
        var pkg = BuildPackage("sender1", courierId: courierId, status: PackageStatus.Delivered);
        pkg.ProofCode = "ABC123";
        db.Packages.Add(pkg);
        await db.SaveChangesAsync();

        var handler = new UploadProofHandler(db);
        var result = await handler.Handle(
            new UploadProofCommand(courierId, pkg.Id, null, null), CancellationToken.None);

        result.ProofCode.Should().Be("ABC123");
    }

    [Fact]
    public async Task UploadProof_InTransit_MarksDelivered()
    {
        using var db = DbContextFactory.Create();
        var courierId = Guid.NewGuid().ToString();
        var pkg = BuildPackage("sender1", courierId: courierId, status: PackageStatus.InTransit);
        db.Packages.Add(pkg);
        await db.SaveChangesAsync();

        var handler = new UploadProofHandler(db);
        var result = await handler.Handle(
            new UploadProofCommand(courierId, pkg.Id, "XYZ789", "https://photo.url"),
            CancellationToken.None);

        result.ProofCode.Should().Be("XYZ789");
        db.Packages.Find(pkg.Id)!.Status.Should().Be(PackageStatus.Delivered);
    }

    // ── ReportFailedDeliveryHandler ───────────────────────────────────────────

    [Fact]
    public async Task ReportFailedDelivery_NotFound_ThrowsKeyNotFound()
    {
        using var db = DbContextFactory.Create();
        var handler = new ReportFailedDeliveryHandler(db);

        var act = () => handler.Handle(
            new ReportFailedDeliveryCommand("courier1", "nonexistent", "Access denied"),
            CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task ReportFailedDelivery_AlreadyDelivered_ThrowsConflict()
    {
        using var db = DbContextFactory.Create();
        var courierId = Guid.NewGuid().ToString();
        var pkg = BuildPackage("sender1", courierId: courierId, status: PackageStatus.Delivered);
        db.Packages.Add(pkg);
        await db.SaveChangesAsync();

        var handler = new ReportFailedDeliveryHandler(db);
        var act = () => handler.Handle(
            new ReportFailedDeliveryCommand(courierId, pkg.Id, "Access denied"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already delivered*");
    }

    [Fact]
    public async Task ReportFailedDelivery_InTransit_MarksReturned()
    {
        using var db = DbContextFactory.Create();
        var courierId = Guid.NewGuid().ToString();
        var pkg = BuildPackage("sender1", courierId: courierId, status: PackageStatus.InTransit);
        db.Packages.Add(pkg);
        await db.SaveChangesAsync();

        var handler = new ReportFailedDeliveryHandler(db);
        await handler.Handle(
            new ReportFailedDeliveryCommand(courierId, pkg.Id, "Access denied"),
            CancellationToken.None);

        var updated = db.Packages.Find(pkg.Id)!;
        updated.Status.Should().Be(PackageStatus.Returned);
        updated.FailedDeliveryReason.Should().Be("Access denied");
    }
}
