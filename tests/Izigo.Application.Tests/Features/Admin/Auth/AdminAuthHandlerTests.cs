using FluentAssertions;
using Izigo.Application.Features.Admin.Auth.Commands;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Izigo.Application.Tests.Features.Admin.Auth;

public class AdminAuthHandlerTests
{
    // SHA256 matching AdminAuthHelpers.Sha256 (file-scoped, duplicated here)
    private static string Sha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLower();
    }

    private static Domain.Entities.Staff BuildStaff(
        string email = "staff@test.com",
        string password = "pass",
        string role = "admin",
        StaffStatus status = StaffStatus.Active,
        bool twoFaEnabled = false) => new()
    {
        Name               = "Test Staff",
        Email              = email,
        RoleKey            = role,
        PasswordHash       = FakeServices.Hasher().Hash(password),
        Status             = status,
        TwoFaEnabled       = twoFaEnabled,
        GrantedPermissions = [],
        RevokedPermissions = [],
        Markets            = ["ci"]
    };

    // ── AdminLoginHandler ─────────────────────────────────────────────────────

    [Fact]
    public async Task AdminLogin_WrongPassword_ReturnsInvalidCredentials()
    {
        using var db = DbContextFactory.Create();
        db.Staff.Add(BuildStaff("admin@test.com", "CorrectPass!"));
        await db.SaveChangesAsync();

        var handler = new AdminLoginHandler(db, FakeServices.Hasher(), FakeServices.Token(), FakeServices.AdminAuthSettings());
        var result  = await handler.Handle(
            new AdminLoginCommand("admin@test.com", "WrongPass!"), CancellationToken.None);

        result.Bundle.Should().BeNull();
        result.ErrorCode.Should().Be("INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task AdminLogin_UnknownEmail_ReturnsInvalidCredentials()
    {
        using var db = DbContextFactory.Create();
        var handler = new AdminLoginHandler(db, FakeServices.Hasher(), FakeServices.Token(), FakeServices.AdminAuthSettings());
        var result  = await handler.Handle(
            new AdminLoginCommand("nobody@test.com", "anything"), CancellationToken.None);

        result.Bundle.Should().BeNull();
        result.ErrorCode.Should().Be("INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task AdminLogin_BlockedAccount_ReturnsAccountBlocked()
    {
        using var db = DbContextFactory.Create();
        db.Staff.Add(BuildStaff(status: StaffStatus.Blocked));
        await db.SaveChangesAsync();

        var handler = new AdminLoginHandler(db, FakeServices.Hasher(), FakeServices.Token(), FakeServices.AdminAuthSettings());
        var result  = await handler.Handle(
            new AdminLoginCommand("staff@test.com", "pass"), CancellationToken.None);

        result.ErrorCode.Should().Be("ACCOUNT_BLOCKED");
    }

    [Fact]
    public async Task AdminLogin_SuspendedAccount_ReturnsAccountSuspended()
    {
        using var db = DbContextFactory.Create();
        db.Staff.Add(BuildStaff(status: StaffStatus.Suspended));
        await db.SaveChangesAsync();

        var handler = new AdminLoginHandler(db, FakeServices.Hasher(), FakeServices.Token(), FakeServices.AdminAuthSettings());
        var result  = await handler.Handle(
            new AdminLoginCommand("staff@test.com", "pass"), CancellationToken.None);

        result.ErrorCode.Should().Be("ACCOUNT_SUSPENDED");
    }

    [Fact]
    public async Task AdminLogin_With2FA_Returns2FAChallenge()
    {
        using var db = DbContextFactory.Create();
        db.Staff.Add(BuildStaff(twoFaEnabled: true));
        await db.SaveChangesAsync();

        var handler = new AdminLoginHandler(db, FakeServices.Hasher(), FakeServices.Token(), FakeServices.AdminAuthSettings());
        var result  = await handler.Handle(
            new AdminLoginCommand("staff@test.com", "pass"), CancellationToken.None);

        result.Bundle.Should().BeNull();
        result.Challenge.Should().NotBeNull();
        result.Challenge!.Requires2fa.Should().BeTrue();
        result.Challenge.ChallengeToken.Should().NotBeNullOrEmpty();
        result.ErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task AdminLogin_Without2FA_ValidCredentials_ReturnsBundle()
    {
        using var db = DbContextFactory.Create();
        db.Staff.Add(BuildStaff());
        await db.SaveChangesAsync();

        var handler = new AdminLoginHandler(db, FakeServices.Hasher(), FakeServices.Token(), FakeServices.AdminAuthSettings());
        var result  = await handler.Handle(
            new AdminLoginCommand("staff@test.com", "pass"), CancellationToken.None);

        result.Bundle.Should().NotBeNull();
        result.Bundle!.AccessToken.Should().Be("test_admin_access_token");
        result.ErrorCode.Should().BeNull();
    }

    // ── Verify2FaHandler ─────────────────────────────────────────────────────

    [Fact]
    public async Task Verify2Fa_ExpiredChallenge_Returns2FaExpired()
    {
        using var db = DbContextFactory.Create();
        var staff = BuildStaff(twoFaEnabled: true);
        db.Staff.Add(staff);
        await db.SaveChangesAsync();

        var tokenRaw = "test-challenge-token";
        db.TwoFaChallenges.Add(new TwoFaChallenge
        {
            StaffId            = staff.Id,
            ChallengeTokenHash = Sha256(tokenRaw),
            ExpiresAt          = DateTime.UtcNow.AddSeconds(-1),
            IsUsed             = false
        });
        await db.SaveChangesAsync();

        var handler = new Verify2FaHandler(db, FakeServices.Token());
        var result  = await handler.Handle(new Verify2FaCommand(tokenRaw, "123456"), CancellationToken.None);

        result.ErrorCode.Should().Be("2FA_EXPIRED");
    }

    [Fact]
    public async Task Verify2Fa_NonExistentChallenge_Returns2FaExpired()
    {
        using var db = DbContextFactory.Create();
        var handler = new Verify2FaHandler(db, FakeServices.Token());
        var result  = await handler.Handle(
            new Verify2FaCommand("nonexistent-token", "123456"), CancellationToken.None);

        result.ErrorCode.Should().Be("2FA_EXPIRED");
    }

    [Fact]
    public async Task Verify2Fa_MaxAttempts_Returns2FaLocked()
    {
        using var db = DbContextFactory.Create();
        var staff = BuildStaff(twoFaEnabled: true);
        db.Staff.Add(staff);
        await db.SaveChangesAsync();

        var tokenRaw = "locked-challenge";
        db.TwoFaChallenges.Add(new TwoFaChallenge
        {
            StaffId            = staff.Id,
            ChallengeTokenHash = Sha256(tokenRaw),
            ExpiresAt          = DateTime.UtcNow.AddMinutes(5),
            IsUsed             = false,
            AttemptCount       = 5
        });
        await db.SaveChangesAsync();

        var handler = new Verify2FaHandler(db, FakeServices.Token());
        var result  = await handler.Handle(new Verify2FaCommand(tokenRaw, "wrong"), CancellationToken.None);

        result.ErrorCode.Should().Be("2FA_LOCKED");
    }

    // ── AdminRefreshHandler ───────────────────────────────────────────────────

    [Fact]
    public async Task AdminRefresh_InvalidToken_ReturnsInvalidRefreshToken()
    {
        using var db = DbContextFactory.Create();
        var handler = new AdminRefreshHandler(db, FakeServices.Token());
        var result  = await handler.Handle(
            new AdminRefreshCommand("nonexistent-token"), CancellationToken.None);

        result.ErrorCode.Should().Be("INVALID_REFRESH_TOKEN");
    }

    [Fact]
    public async Task AdminRefresh_ExpiredToken_ReturnsInvalidRefreshToken()
    {
        using var db = DbContextFactory.Create();
        var staff = BuildStaff();
        db.Staff.Add(staff);
        await db.SaveChangesAsync();

        db.StaffRefreshTokens.Add(new StaffRefreshToken
        {
            StaffId           = staff.Id,
            Token             = "expired-refresh",
            ExpiresAt         = DateTime.UtcNow.AddHours(-1),
            AbsoluteCreatedAt = DateTime.UtcNow.AddHours(-13),
            IsRevoked         = false
        });
        await db.SaveChangesAsync();

        var handler = new AdminRefreshHandler(db, FakeServices.Token());
        var result  = await handler.Handle(new AdminRefreshCommand("expired-refresh"), CancellationToken.None);

        result.ErrorCode.Should().Be("INVALID_REFRESH_TOKEN");
    }

    [Fact]
    public async Task AdminRefresh_AbsoluteCapExceeded_ReturnsSessionExpired()
    {
        using var db = DbContextFactory.Create();
        var staff = BuildStaff();
        db.Staff.Add(staff);
        await db.SaveChangesAsync();

        db.StaffRefreshTokens.Add(new StaffRefreshToken
        {
            StaffId           = staff.Id,
            Staff             = staff,
            Token             = "old-but-valid",
            ExpiresAt         = DateTime.UtcNow.AddHours(1),
            AbsoluteCreatedAt = DateTime.UtcNow.AddHours(-13),
            IsRevoked         = false
        });
        await db.SaveChangesAsync();

        var handler = new AdminRefreshHandler(db, FakeServices.Token());
        var result  = await handler.Handle(new AdminRefreshCommand("old-but-valid"), CancellationToken.None);

        result.ErrorCode.Should().Be("SESSION_EXPIRED");
    }

    [Fact]
    public async Task AdminRefresh_ValidToken_RotatesAndReturnsNewBundle()
    {
        using var db = DbContextFactory.Create();
        var staff = BuildStaff();
        db.Staff.Add(staff);
        await db.SaveChangesAsync();

        db.StaffRefreshTokens.Add(new StaffRefreshToken
        {
            StaffId           = staff.Id,
            Staff             = staff,
            Token             = "valid-refresh-token",
            ExpiresAt         = DateTime.UtcNow.AddHours(6),
            AbsoluteCreatedAt = DateTime.UtcNow.AddHours(-2),
            IsRevoked         = false
        });
        await db.SaveChangesAsync();

        var handler = new AdminRefreshHandler(db, FakeServices.Token());
        var result  = await handler.Handle(new AdminRefreshCommand("valid-refresh-token"), CancellationToken.None);

        result.Bundle.Should().NotBeNull();
        result.ErrorCode.Should().BeNull();
        db.StaffRefreshTokens.First(r => r.Token == "valid-refresh-token").IsRevoked.Should().BeTrue();
        db.StaffRefreshTokens.Should().HaveCount(2);
    }
}
