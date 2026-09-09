using FluentAssertions;
using Izigo.Application.Features.Profile.Commands;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Enums;
using Xunit;
using WalletEntity = Izigo.Domain.Entities.Wallet;
using UserEntity = Izigo.Domain.Entities.User;
using EmergencyContactEntity = Izigo.Domain.Entities.EmergencyContact;
using RefreshTokenEntity = Izigo.Domain.Entities.RefreshToken;

namespace Izigo.Application.Tests.Features.Profile;

public class ProfileCommandTests
{
    private static UserEntity BuildUser(string phone = "+2250700000099", UserRole role = UserRole.Rider) => new()
    {
        FirstName = "Test",
        LastName  = "User",
        Phone     = phone,
        Role      = role,
        Language  = "fr",
        Rating    = 5.0m
    };

    // ── UpdateProfileHandler ──────────────────────────────────────────────────

    [Fact]
    public async Task UpdateProfile_UserNotFound_ThrowsKeyNotFound()
    {
        using var db = DbContextFactory.Create();
        var handler = new UpdateProfileHandler(db);

        var act = () => handler.Handle(
            new UpdateProfileCommand("nonexistent", "Alice", null, null, null, null, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task UpdateProfile_EmailAlreadyTaken_ThrowsArgumentException()
    {
        using var db = DbContextFactory.Create();
        UserEntity user1 = BuildUser("+2250700000011");
        UserEntity user2 = BuildUser("+2250700000012");
        user2.Email = "taken@example.com";
        db.Users.AddRange(user1, user2);
        await db.SaveChangesAsync();

        var handler = new UpdateProfileHandler(db);
        var act = () => handler.Handle(
            new UpdateProfileCommand(user1.Id, null, null, "taken@example.com", null, null, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Email is already in use*");
    }

    [Fact]
    public async Task UpdateProfile_UnsupportedLanguage_ThrowsArgumentException()
    {
        using var db = DbContextFactory.Create();
        var user = BuildUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var handler = new UpdateProfileHandler(db);
        var act = () => handler.Handle(
            new UpdateProfileCommand(user.Id, null, null, null, "de", null, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Unsupported language*");
    }

    [Fact]
    public async Task UpdateProfile_InvalidDateOfBirth_ThrowsArgumentException()
    {
        using var db = DbContextFactory.Create();
        var user = BuildUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var handler = new UpdateProfileHandler(db);
        var act = () => handler.Handle(
            new UpdateProfileCommand(user.Id, null, null, null, null, null, "not-a-date"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*date_of_birth*");
    }

    [Fact]
    public async Task UpdateProfile_ValidFields_UpdatesUser()
    {
        using var db = DbContextFactory.Create();
        var user = BuildUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var handler = new UpdateProfileHandler(db);
        var result = await handler.Handle(
            new UpdateProfileCommand(user.Id, "Alice", "Smith", null, "en", "female", "1990-05-20"),
            CancellationToken.None);

        result.Should().NotBeNull();
        var updated = db.Users.Find(user.Id)!;
        updated.FirstName.Should().Be("Alice");
        updated.LastName.Should().Be("Smith");
        updated.Language.Should().Be("en");
        updated.Gender.Should().Be("female");
        updated.DateOfBirth.Should().Be(new DateOnly(1990, 5, 20));
    }

    [Fact]
    public async Task UpdateProfile_SameEmail_DoesNotThrow()
    {
        using var db = DbContextFactory.Create();
        var user = BuildUser();
        user.Email = "same@example.com";
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var handler = new UpdateProfileHandler(db);
        // Updating with the same email should not throw uniqueness error
        await handler.Invoking(h => h.Handle(
            new UpdateProfileCommand(user.Id, null, null, "same@example.com", null, null, null),
            CancellationToken.None)).Should().NotThrowAsync();
    }

    // ── AddEmergencyContactHandler ────────────────────────────────────────────

    [Fact]
    public async Task AddEmergencyContact_MaxContacts_ThrowsConflict()
    {
        using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid().ToString();
        for (var i = 0; i < 5; i++)
            db.EmergencyContacts.Add(new EmergencyContactEntity
            {
                UserId = userId, Name = $"Contact {i}", Phone = $"+225070000000{i}"
            });
        await db.SaveChangesAsync();

        var handler = new AddEmergencyContactHandler(db);
        var act = () => handler.Handle(
            new AddEmergencyContactCommand(userId, "Extra Contact", "+2250700000099", null, false),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Maximum 5*");
    }

    [Fact]
    public async Task AddEmergencyContact_ValidPhone_CreatesContact()
    {
        using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid().ToString();

        var handler = new AddEmergencyContactHandler(db);
        var result = await handler.Handle(
            new AddEmergencyContactCommand(userId, "Jane Doe", "+2250700000001", "Sister", true),
            CancellationToken.None);

        result.Name.Should().Be("Jane Doe");
        result.Phone.Should().Be("+2250700000001");
        result.Relationship.Should().Be("Sister");
        result.NotifyOnTripStart.Should().BeTrue();
        db.EmergencyContacts.Should().HaveCount(1);
    }

    // ── DeleteEmergencyContactHandler ─────────────────────────────────────────

    [Fact]
    public async Task DeleteEmergencyContact_NotFound_ThrowsKeyNotFound()
    {
        using var db = DbContextFactory.Create();
        var handler = new DeleteEmergencyContactHandler(db);

        var act = () => handler.Handle(
            new DeleteEmergencyContactCommand("user1", "nonexistent"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*Emergency contact not found*");
    }

    [Fact]
    public async Task DeleteEmergencyContact_WrongUser_ThrowsKeyNotFound()
    {
        using var db = DbContextFactory.Create();
        var contact = new EmergencyContactEntity
        {
            UserId = "user-A", Name = "John", Phone = "+2250700000001"
        };
        db.EmergencyContacts.Add(contact);
        await db.SaveChangesAsync();

        var handler = new DeleteEmergencyContactHandler(db);
        var act = () => handler.Handle(
            new DeleteEmergencyContactCommand("user-B", contact.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task DeleteEmergencyContact_Success_RemovesContact()
    {
        using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid().ToString();
        var contact = new EmergencyContactEntity
        {
            UserId = userId, Name = "John", Phone = "+2250700000001"
        };
        db.EmergencyContacts.Add(contact);
        await db.SaveChangesAsync();

        var handler = new DeleteEmergencyContactHandler(db);
        await handler.Handle(
            new DeleteEmergencyContactCommand(userId, contact.Id), CancellationToken.None);

        db.EmergencyContacts.Should().BeEmpty();
    }

    // ── CloseAccountHandler ───────────────────────────────────────────────────

    [Fact]
    public async Task CloseAccount_UserNotFound_ThrowsKeyNotFound()
    {
        using var db = DbContextFactory.Create();
        var handler = new CloseAccountHandler(db, FakeServices.Hasher());

        var act = () => handler.Handle(
            new CloseAccountCommand("nonexistent", "leaving", null), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task CloseAccount_WalletBalanceExists_ThrowsConflict()
    {
        using var db = DbContextFactory.Create();
        var user = BuildUser();
        db.Users.Add(user);
        db.Wallets.Add(new WalletEntity { UserId = user.Id, Balance = 1500, Currency = "XOF",
            MinTopup = 500, MaxBalance = 500_000 });
        await db.SaveChangesAsync();

        var handler = new CloseAccountHandler(db, FakeServices.Hasher());
        var act = () => handler.Handle(
            new CloseAccountCommand(user.Id, "leaving", null), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Wallet balance*");
    }

    [Fact]
    public async Task CloseAccount_ActiveTrip_ThrowsConflict()
    {
        using var db = DbContextFactory.Create();
        var user = BuildUser();
        db.Users.Add(user);
        db.Trips.Add(new Domain.Entities.Trip
        {
            Code = "ZR-0001", RiderId = user.Id, Market = "ci", Currency = "XOF",
            Vertical = Vertical.Ride, ServiceClass = ServiceClass.ZenCar,
            JobState = JobState.Accepted,
            PickupLat = 5.3m, PickupLng = -4.0m, PickupLabel = "A",
            DropoffLat = 5.4m, DropoffLng = -4.1m, DropoffLabel = "B",
            PaymentMethod = PaymentMethod.Cash
        });
        await db.SaveChangesAsync();

        var handler = new CloseAccountHandler(db, FakeServices.Hasher());
        var act = () => handler.Handle(
            new CloseAccountCommand(user.Id, "leaving", null), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*trip is in progress*");
    }

    [Fact]
    public async Task CloseAccount_NoBlockers_MarksDeletionAndRevokesTokens()
    {
        using var db = DbContextFactory.Create();
        var user = BuildUser();
        db.Users.Add(user);
        db.RefreshTokens.Add(new RefreshTokenEntity
        {
            UserId = user.Id, Token = "tok1", ExpiresAt = DateTime.UtcNow.AddDays(30)
        });
        await db.SaveChangesAsync();

        var handler = new CloseAccountHandler(db, FakeServices.Hasher());
        await handler.Handle(
            new CloseAccountCommand(user.Id, "no longer need it", null), CancellationToken.None);

        var updated = db.Users.Find(user.Id)!;
        updated.DeletionRequested.Should().BeTrue();
        updated.DeletionReason.Should().Be("no longer need it");
        db.RefreshTokens.First().IsRevoked.Should().BeTrue();
    }
}
