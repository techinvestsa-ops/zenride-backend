using FluentAssertions;
using Izigo.Application.Features.Auth.Commands;
using Izigo.Application.Features.Auth.Dtos;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Xunit;

namespace Izigo.Application.Tests.Features.Auth;

public class AuthHandlerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static OtpRecord BuildOtpRecord(string token, string code, string phone = "+2250700000001",
        bool isUsed = false, int attempts = 0, OtpPurpose purpose = OtpPurpose.Login) => new()
    {
        OtpToken     = token,
        Identifier   = phone,
        CodeHash     = RequestOtpHandler.HashCode(code),
        Purpose      = purpose,
        Role         = "rider",
        IsUsed       = isUsed,
        AttemptCount = attempts,
        ExpiresAt    = DateTime.UtcNow.AddMinutes(5)
    };

    private static DeviceDto Device() =>
        new("test-device-id", "android", "1.0.0", null, null, null, null, null);

    // ── RequestOtpHandler ────────────────────────────────────────────────────

    [Fact]
    public async Task RequestOtp_RateLimits_WhenTooManyRequests()
    {
        using var db = DbContextFactory.Create();
        var phone = "+2250700000001";

        for (var i = 0; i < 5; i++)
            db.OtpRecords.Add(new OtpRecord
            {
                Identifier = phone, CodeHash = "x", Purpose = OtpPurpose.Login, Role = "rider",
                ExpiresAt  = DateTime.UtcNow.AddMinutes(5),
                CreatedAt  = DateTime.UtcNow.AddMinutes(-i)
            });
        await db.SaveChangesAsync();

        var handler = new RequestOtpHandler(db, FakeServices.Otp(), FakeServices.OtpSettings());
        var act = () => handler.Handle(new RequestOtpCommand(phone, "rider", "Login"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Too many OTP requests*");
    }

    [Fact]
    public async Task RequestOtp_DetectsNewUser_WhenPhoneNotRegistered()
    {
        using var db = DbContextFactory.Create();
        var handler = new RequestOtpHandler(db, FakeServices.Otp(), FakeServices.OtpSettings());

        var result = await handler.Handle(new RequestOtpCommand("+2250700000002", "rider", "Login"), CancellationToken.None);

        result.IsNewUser.Should().BeTrue();
    }

    [Fact]
    public async Task RequestOtp_DetectsExistingUser_WhenPhoneRegistered()
    {
        using var db = DbContextFactory.Create();
        db.Users.Add(new User { Phone = "+2250700000003", Role = UserRole.Rider, Status = UserStatus.Active });
        await db.SaveChangesAsync();

        var handler = new RequestOtpHandler(db, FakeServices.Otp(), FakeServices.OtpSettings());
        var result  = await handler.Handle(new RequestOtpCommand("+2250700000003", "rider", "Login"), CancellationToken.None);

        result.IsNewUser.Should().BeFalse();
    }

    [Fact]
    public async Task RequestOtp_InvalidatesExistingActiveOtp()
    {
        using var db = DbContextFactory.Create();
        var phone     = "+2250700000004";
        var oldRecord = BuildOtpRecord("old-token", "111111", phone);
        db.OtpRecords.Add(oldRecord);
        await db.SaveChangesAsync();

        var handler = new RequestOtpHandler(db, FakeServices.Otp(), FakeServices.OtpSettings());
        await handler.Handle(new RequestOtpCommand(phone, "rider", "Login"), CancellationToken.None);

        db.OtpRecords.First(r => r.OtpToken == "old-token").IsUsed.Should().BeTrue();
    }

    [Fact]
    public async Task RequestOtp_CreatesOtpRecord_WithCorrectHash()
    {
        using var db = DbContextFactory.Create();
        var phone   = "+2250700000005";
        var handler = new RequestOtpHandler(db, FakeServices.Otp("654321"), FakeServices.OtpSettings());

        var result = await handler.Handle(new RequestOtpCommand(phone, "rider", "Login"), CancellationToken.None);

        var record = db.OtpRecords.First(r => r.OtpToken == result.OtpToken);
        record.CodeHash.Should().Be(RequestOtpHandler.HashCode("654321"));
    }

    // ── VerifyOtpHandler ─────────────────────────────────────────────────────

    [Fact]
    public async Task VerifyOtp_InvalidCode_IncrementsAttemptCount()
    {
        using var db = DbContextFactory.Create();
        db.OtpRecords.Add(BuildOtpRecord("tok1", "123456"));
        await db.SaveChangesAsync();

        var handler = new VerifyOtpHandler(db, FakeServices.Token());
        var act = () => handler.Handle(new VerifyOtpCommand("tok1", "000000", Device()), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Invalid code*");
        db.OtpRecords.First(r => r.OtpToken == "tok1").AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task VerifyOtp_MaxAttemptsReached_ThrowsConflict()
    {
        using var db = DbContextFactory.Create();
        db.OtpRecords.Add(BuildOtpRecord("tok2", "123456", attempts: 5));
        await db.SaveChangesAsync();

        var handler = new VerifyOtpHandler(db, FakeServices.Token());
        var act = () => handler.Handle(new VerifyOtpCommand("tok2", "123456", Device()), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Too many incorrect attempts*");
    }

    [Fact]
    public async Task VerifyOtp_ExpiredToken_ThrowsConflict()
    {
        using var db = DbContextFactory.Create();
        db.OtpRecords.Add(new OtpRecord
        {
            OtpToken   = "tok3", Identifier = "+2250700000001",
            CodeHash   = RequestOtpHandler.HashCode("123456"),
            Purpose    = OtpPurpose.Login, Role = "rider",
            ExpiresAt  = DateTime.UtcNow.AddMinutes(-1)
        });
        await db.SaveChangesAsync();

        var handler = new VerifyOtpHandler(db, FakeServices.Token());
        var act = () => handler.Handle(new VerifyOtpCommand("tok3", "123456", Device()), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*OTP has expired*");
    }

    [Fact]
    public async Task VerifyOtp_AlreadyUsed_ThrowsConflict()
    {
        using var db = DbContextFactory.Create();
        db.OtpRecords.Add(BuildOtpRecord("tok4", "123456", isUsed: true));
        await db.SaveChangesAsync();

        var handler = new VerifyOtpHandler(db, FakeServices.Token());
        var act = () => handler.Handle(new VerifyOtpCommand("tok4", "123456", Device()), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*OTP has expired*");
    }

    [Fact]
    public async Task VerifyOtp_ValidCode_CreatesNewUser_AndWallet()
    {
        using var db = DbContextFactory.Create();
        var phone = "+2250700000010";
        db.OtpRecords.Add(BuildOtpRecord("tok5", "123456", phone));
        await db.SaveChangesAsync();

        var handler = new VerifyOtpHandler(db, FakeServices.Token());
        var result  = await handler.Handle(new VerifyOtpCommand("tok5", "123456", Device()), CancellationToken.None);

        result.AccessToken.Should().Be("test_access_token");
        db.Users.Should().Contain(u => u.Phone == phone);
        var userId = db.Users.First(u => u.Phone == phone).Id;
        db.Wallets.Should().Contain(w => w.UserId == userId);
    }

    [Fact]
    public async Task VerifyOtp_ValidCode_MarksTokenUsed()
    {
        using var db = DbContextFactory.Create();
        var phone = "+2250700000011";
        db.OtpRecords.Add(BuildOtpRecord("tok6", "123456", phone));
        await db.SaveChangesAsync();

        var handler = new VerifyOtpHandler(db, FakeServices.Token());
        await handler.Handle(new VerifyOtpCommand("tok6", "123456", Device()), CancellationToken.None);

        db.OtpRecords.First(r => r.OtpToken == "tok6").IsUsed.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyOtp_SuspendedUser_ThrowsConflict()
    {
        using var db = DbContextFactory.Create();
        var phone = "+2250700000012";
        db.OtpRecords.Add(BuildOtpRecord("tok7", "123456", phone));
        var user = new User
        {
            Phone = phone, Role = UserRole.Rider, Status = UserStatus.Suspended,
            SuspensionReason = "Policy violation"
        };
        db.Users.Add(user);
        db.Wallets.Add(new Izigo.Domain.Entities.Wallet { UserId = user.Id, Currency = "XOF" });
        await db.SaveChangesAsync();

        var handler = new VerifyOtpHandler(db, FakeServices.Token());
        var act = () => handler.Handle(new VerifyOtpCommand("tok7", "123456", Device()), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*suspended*");
    }

    // ── LoginHandler ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_ValidCredentials_ReturnsAuthBundle()
    {
        using var db = DbContextFactory.Create();
        var hasher = FakeServices.Hasher();
        var user   = new User
        {
            Phone = "+2250700000020", Role = UserRole.Rider, Status = UserStatus.Active,
            PasswordHash = hasher.Hash("MyPassword123!")
        };
        db.Users.Add(user);
        db.Wallets.Add(new Izigo.Domain.Entities.Wallet { UserId = user.Id, Currency = "XOF" });
        await db.SaveChangesAsync();

        var handler = new LoginHandler(db, FakeServices.Token(), hasher);
        var result  = await handler.Handle(
            new LoginCommand("+2250700000020", "MyPassword123!", "rider", Device()), CancellationToken.None);

        result.AccessToken.Should().Be("test_access_token");
    }

    [Fact]
    public async Task Login_WrongPassword_ThrowsUnauthorized()
    {
        using var db = DbContextFactory.Create();
        var hasher = FakeServices.Hasher();
        db.Users.Add(new User
        {
            Phone = "+2250700000021", Role = UserRole.Rider, Status = UserStatus.Active,
            PasswordHash = hasher.Hash("CorrectPassword!")
        });
        await db.SaveChangesAsync();

        var handler = new LoginHandler(db, FakeServices.Token(), hasher);
        var act = () => handler.Handle(
            new LoginCommand("+2250700000021", "WrongPassword!", "rider", Device()), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Login_UnknownUser_ThrowsUnauthorized()
    {
        using var db = DbContextFactory.Create();
        var handler = new LoginHandler(db, FakeServices.Token(), FakeServices.Hasher());
        var act = () => handler.Handle(
            new LoginCommand("+2250700000099", "anything", "rider", Device()), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Login_SuspendedUser_ThrowsUnauthorized()
    {
        using var db = DbContextFactory.Create();
        var hasher = FakeServices.Hasher();
        db.Users.Add(new User
        {
            Phone = "+2250700000022", Role = UserRole.Rider, Status = UserStatus.Suspended,
            SuspensionReason = "Test", PasswordHash = hasher.Hash("pass")
        });
        await db.SaveChangesAsync();

        var handler = new LoginHandler(db, FakeServices.Token(), hasher);
        var act = () => handler.Handle(
            new LoginCommand("+2250700000022", "pass", "rider", Device()), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>().WithMessage("*ACCOUNT_SUSPENDED*");
    }

    [Fact]
    public async Task Login_BlockedUser_ThrowsUnauthorized()
    {
        using var db = DbContextFactory.Create();
        var hasher = FakeServices.Hasher();
        db.Users.Add(new User
        {
            Phone = "+2250700000023", Role = UserRole.Rider, Status = UserStatus.Blocked,
            PasswordHash = hasher.Hash("pass")
        });
        await db.SaveChangesAsync();

        var handler = new LoginHandler(db, FakeServices.Token(), hasher);
        var act = () => handler.Handle(
            new LoginCommand("+2250700000023", "pass", "rider", Device()), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>().WithMessage("*ACCOUNT_BLOCKED*");
    }

    // ── ForgotPasswordHandler (user) ─────────────────────────────────────────

    [Fact]
    public async Task ForgotPassword_UnknownIdentifier_StillReturns200Shape()
    {
        using var db = DbContextFactory.Create();
        var handler = new ForgotPasswordHandler(db, FakeServices.Otp(), FakeServices.OtpSettings());

        var result = await handler.Handle(
            new ForgotPasswordCommand("+2250700000099", "rider"), CancellationToken.None);

        result.ResetToken.Should().NotBeNullOrEmpty();
        result.ExpiresIn.Should().BeGreaterThan(0);
        db.OtpRecords.Should().BeEmpty();
    }

    [Fact]
    public async Task ForgotPassword_KnownUser_CreatesOtpRecord()
    {
        using var db = DbContextFactory.Create();
        db.Users.Add(new User { Phone = "+2250700000030", Role = UserRole.Rider, Status = UserStatus.Active });
        await db.SaveChangesAsync();

        var handler = new ForgotPasswordHandler(db, FakeServices.Otp(), FakeServices.OtpSettings());
        await handler.Handle(new ForgotPasswordCommand("+2250700000030", "rider"), CancellationToken.None);

        db.OtpRecords.Should().HaveCount(1);
        db.OtpRecords.First().Identifier.Should().Be("+2250700000030");
    }

    // ── ResendOtpHandler ─────────────────────────────────────────────────────

    [Fact]
    public async Task ResendOtp_CooldownNotElapsed_ThrowsConflict()
    {
        using var db = DbContextFactory.Create();
        db.OtpRecords.Add(new OtpRecord
        {
            OtpToken   = "resend1", Identifier = "+2250700000040",
            CodeHash   = RequestOtpHandler.HashCode("123456"),
            Purpose    = OtpPurpose.Login, Role = "rider",
            ExpiresAt  = DateTime.UtcNow.AddMinutes(5),
            CreatedAt  = DateTime.UtcNow.AddSeconds(-10) // 10s ago, cooldown = 60s
        });
        await db.SaveChangesAsync();

        var handler = new ResendOtpHandler(db, FakeServices.Otp(), FakeServices.OtpSettings());
        var act = () => handler.Handle(new ResendOtpCommand("resend1", null), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Please wait*");
    }

    [Fact]
    public async Task ResendOtp_ExpiredSession_ThrowsConflict()
    {
        using var db = DbContextFactory.Create();
        db.OtpRecords.Add(new OtpRecord
        {
            OtpToken  = "resend2", Identifier = "+2250700000041",
            CodeHash  = RequestOtpHandler.HashCode("123456"),
            Purpose   = OtpPurpose.Login, Role = "rider",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = DateTime.UtcNow.AddMinutes(-10)
        });
        await db.SaveChangesAsync();

        var handler = new ResendOtpHandler(db, FakeServices.Otp(), FakeServices.OtpSettings());
        var act = () => handler.Handle(new ResendOtpCommand("resend2", null), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*expired*");
    }

    [Fact]
    public async Task ResendOtp_AfterCooldown_UpdatesCodeHash()
    {
        using var db = DbContextFactory.Create();
        db.OtpRecords.Add(new OtpRecord
        {
            OtpToken  = "resend3", Identifier = "+2250700000042",
            CodeHash  = RequestOtpHandler.HashCode("111111"),
            Purpose   = OtpPurpose.Login, Role = "rider",
            ExpiresAt = DateTime.UtcNow.AddMinutes(4),
            CreatedAt = DateTime.UtcNow.AddSeconds(-120) // past cooldown
        });
        await db.SaveChangesAsync();

        var handler = new ResendOtpHandler(db, FakeServices.Otp("999999"), FakeServices.OtpSettings());
        await handler.Handle(new ResendOtpCommand("resend3", null), CancellationToken.None);

        db.OtpRecords.First(r => r.OtpToken == "resend3").CodeHash
            .Should().Be(RequestOtpHandler.HashCode("999999"));
    }
}
