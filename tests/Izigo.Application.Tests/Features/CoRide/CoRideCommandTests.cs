using FluentAssertions;
using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.CoRide.Commands;
using Izigo.Application.Features.CoRide.Dtos;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Moq;
using Xunit;

namespace Izigo.Application.Tests.Features.CoRide;

public class CoRideCommandTests
{
    // ── PublishListingHandler ─────────────────────────────────────────────────

    [Fact]
    public async Task PublishListing_DepartureTooSoon_ThrowsValidationError()
    {
        using var db = DbContextFactory.Create();
        var user = new User { Phone = "+2250700000001", Role = UserRole.Driver, Status = UserStatus.Active };
        db.Users.Add(user);
        var profile = new DriverProfile
        {
            UserId = user.Id, KycStatus = KycStatus.Approved, OnboardingComplete = true
        };
        db.DriverProfiles.Add(profile);
        db.Vehicles.Add(new Vehicle
        {
            DriverId = user.Id, Make = "Toyota", Model = "Corolla",
            Plate = "AB-001", IsActive = true, Year = 2020
        });
        await db.SaveChangesAsync();

        var handler = new PublishListingHandler(db, FakeServices.Geo(), FakeServices.AppSettings(), FakeServices.Realtime(), FakeServices.Push());
        var act = () => handler.Handle(new PublishListingCommand(user.Id, new PublishListingRequest(
            FromLat: 5.3, FromLng: -4.0, FromLabel: "Abidjan",
            ToLat: 5.4, ToLng: -4.1, ToLabel: "Yopougon",
            DepartureAt: DateTime.UtcNow.AddMinutes(5), // less than 10 min default
            SeatsTotal: 2, PricePerSeat: 1000, IsEco: false, IsRecurring: null)),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Departure must be at least*");
    }

    [Fact]
    public async Task PublishListing_ValidDeparture_CreatesListing()
    {
        using var db = DbContextFactory.Create();
        var user = new User { Phone = "+2250700000002", Role = UserRole.Driver, Status = UserStatus.Active };
        db.Users.Add(user);
        db.DriverProfiles.Add(new DriverProfile
        {
            UserId = user.Id, KycStatus = KycStatus.Approved, OnboardingComplete = true
        });
        db.Vehicles.Add(new Vehicle
        {
            DriverId = user.Id, Make = "Toyota", Model = "Camry",
            Plate = "AB-002", IsActive = true, Year = 2021
        });
        await db.SaveChangesAsync();

        var handler = new PublishListingHandler(db, FakeServices.Geo(), FakeServices.AppSettings(), FakeServices.Realtime(), FakeServices.Push());
        var result = await handler.Handle(new PublishListingCommand(user.Id, new PublishListingRequest(
            FromLat: 5.3, FromLng: -4.0, FromLabel: "Abidjan",
            ToLat: 5.4, ToLng: -4.1, ToLabel: "Yopougon",
            DepartureAt: DateTime.UtcNow.AddHours(2),
            SeatsTotal: 3, PricePerSeat: 1500, IsEco: false, IsRecurring: null)),
            CancellationToken.None);

        result.TripFare.Should().Be(4_500);
        result.PricePerSeat.Should().Be(1_500);
        db.CoRideListings.Should().HaveCount(1);
    }

    [Fact]
    public async Task PublishListing_NotifiesRiderWithMatchingOpenRequest()
    {
        using var db = DbContextFactory.Create();
        var user = new User { Phone = "+2250700000003", Role = UserRole.Driver, Status = UserStatus.Active };
        db.Users.Add(user);
        db.DriverProfiles.Add(new DriverProfile
        {
            UserId = user.Id, KycStatus = KycStatus.Approved, OnboardingComplete = true
        });
        var departure = DateTime.UtcNow.AddHours(2);
        var nearby = new CoRideRequest
        {
            RiderId = "rider-near", PickupLat = 5.301m, PickupLng = -4.001m,
            DropoffLat = 5.401m, DropoffLng = -4.101m, SeatsNeeded = 1,
            DepartureWindowFrom = departure.AddMinutes(-20), DepartureWindowTo = departure.AddMinutes(20),
            ExpiresAt = departure.AddHours(1),
        };
        var farAway = new CoRideRequest
        {
            RiderId = "rider-far", PickupLat = 6.5m, PickupLng = -5.0m,
            DropoffLat = 5.401m, DropoffLng = -4.101m, SeatsNeeded = 1,
            DepartureWindowFrom = departure.AddMinutes(-20), DepartureWindowTo = departure.AddMinutes(20),
            ExpiresAt = departure.AddHours(1),
        };
        db.CoRideRequests.AddRange(nearby, farAway);
        await db.SaveChangesAsync();

        var realtime = new Mock<IRealtimeService>();
        var handler = new PublishListingHandler(db, FakeServices.Geo(), FakeServices.AppSettings(), realtime.Object, FakeServices.Push());
        await handler.Handle(new PublishListingCommand(user.Id, new PublishListingRequest(
            FromLat: 5.3, FromLng: -4.0, FromLabel: "Abidjan",
            ToLat: 5.4, ToLng: -4.1, ToLabel: "Yopougon",
            DepartureAt: departure,
            SeatsTotal: 3, PricePerSeat: 1500, IsEco: false, IsRecurring: null)),
            CancellationToken.None);

        realtime.Verify(r => r.PublishToUserAsync("rider-near", "coride.request_matched",
            It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
        realtime.Verify(r => r.PublishToUserAsync("rider-far", It.IsAny<string>(),
            It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BookSeats_NotifiesDriver()
    {
        using var db = DbContextFactory.Create();
        var driver = new User { Phone = "+2250700000004", Role = UserRole.Driver, Status = UserStatus.Active };
        db.Users.Add(driver);
        db.DriverProfiles.Add(new DriverProfile
        {
            UserId = driver.Id, KycStatus = KycStatus.Approved, OnboardingComplete = true
        });
        var listing = new CoRideListing
        {
            DriverId = driver.Id, FromLat = 5.3m, FromLng = -4.0m, FromLabel = "Abidjan",
            ToLat = 5.4m, ToLng = -4.1m, ToLabel = "Yopougon",
            DepartureAt = DateTime.UtcNow.AddHours(2), SeatsTotal = 3,
            PricePerSeat = 1500, Currency = "XOF", Status = "open",
        };
        db.CoRideListings.Add(listing);
        await db.SaveChangesAsync();

        var realtime = new Mock<IRealtimeService>();
        var handler = new BookSeatsHandler(db, realtime.Object, FakeServices.Push());
        await handler.Handle(new BookSeatsCommand("rider-1", new BookSeatsRequest(
            ListingId: listing.Id, Seats: 2, PaymentMethod: "cash", PromoCode: null), null),
            CancellationToken.None);

        realtime.Verify(r => r.PublishToDriverAsync(driver.Id, "coride.booking_received",
            It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── CreateMatchRequestHandler ─────────────────────────────────────────────

    [Fact]
    public async Task CreateMatchRequest_CancelsExistingActiveRequest()
    {
        using var db = DbContextFactory.Create();
        var riderId = Guid.NewGuid().ToString();
        var existingRequest = new CoRideRequest
        {
            RiderId             = riderId,
            Status              = CoRideRequestStatus.Searching,
            PickupLat           = 5.3m, PickupLng = -4.0m, PickupLabel = "A",
            DropoffLat          = 5.4m, DropoffLng = -4.1m, DropoffLabel = "B",
            SeatsNeeded         = 1,
            DepartureWindowFrom = DateTime.UtcNow.AddHours(1),
            DepartureWindowTo   = DateTime.UtcNow.AddHours(2),
            ExpiresAt           = DateTime.UtcNow.AddHours(3)
        };
        db.CoRideRequests.Add(existingRequest);
        await db.SaveChangesAsync();

        var handler = new CreateMatchRequestHandler(db);
        await handler.Handle(new CreateMatchRequestCommand(riderId, new CreateMatchRequest(
            PickupLat: 5.5, PickupLng: -4.2, PickupLabel: "C",
            DropoffLat: 5.6, DropoffLng: -4.3, DropoffLabel: "D",
            SeatsNeeded: 1,
            DepartureWindowFrom: DateTime.UtcNow.AddHours(1),
            DepartureWindowTo: DateTime.UtcNow.AddHours(2))),
            CancellationToken.None);

        db.CoRideRequests.First(r => r.Id == existingRequest.Id).Status
            .Should().Be(CoRideRequestStatus.Cancelled);
        db.CoRideRequests.Count(r => r.Status == CoRideRequestStatus.Searching).Should().Be(1);
    }

    // ── WithdrawRequestHandler ────────────────────────────────────────────────

    [Fact]
    public async Task WithdrawRequest_NotSearching_ThrowsConflict()
    {
        using var db = DbContextFactory.Create();
        var riderId = Guid.NewGuid().ToString();
        var request = new CoRideRequest
        {
            RiderId             = riderId,
            Status              = CoRideRequestStatus.Matched,
            PickupLat           = 5.3m, PickupLng = -4.0m, PickupLabel = "A",
            DropoffLat          = 5.4m, DropoffLng = -4.1m, DropoffLabel = "B",
            SeatsNeeded         = 1,
            DepartureWindowFrom = DateTime.UtcNow.AddHours(1),
            DepartureWindowTo   = DateTime.UtcNow.AddHours(2),
            ExpiresAt           = DateTime.UtcNow.AddHours(3)
        };
        db.CoRideRequests.Add(request);
        await db.SaveChangesAsync();

        var handler = new WithdrawRequestHandler(db);
        var act = () => handler.Handle(
            new WithdrawRequestCommand(riderId, request.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no longer active*");
    }

    [Fact]
    public async Task WithdrawRequest_Searching_CancelsRequest()
    {
        using var db = DbContextFactory.Create();
        var riderId = Guid.NewGuid().ToString();
        var request = new CoRideRequest
        {
            RiderId             = riderId,
            Status              = CoRideRequestStatus.Searching,
            PickupLat           = 5.3m, PickupLng = -4.0m, PickupLabel = "A",
            DropoffLat          = 5.4m, DropoffLng = -4.1m, DropoffLabel = "B",
            SeatsNeeded         = 1,
            DepartureWindowFrom = DateTime.UtcNow.AddHours(1),
            DepartureWindowTo   = DateTime.UtcNow.AddHours(2),
            ExpiresAt           = DateTime.UtcNow.AddHours(3)
        };
        db.CoRideRequests.Add(request);
        await db.SaveChangesAsync();

        var handler = new WithdrawRequestHandler(db);
        await handler.Handle(new WithdrawRequestCommand(riderId, request.Id), CancellationToken.None);

        db.CoRideRequests.First(r => r.Id == request.Id).Status
            .Should().Be(CoRideRequestStatus.Cancelled);
    }
}
