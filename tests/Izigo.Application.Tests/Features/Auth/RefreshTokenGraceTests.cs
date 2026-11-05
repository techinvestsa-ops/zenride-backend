using FluentAssertions;
using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Auth.Commands;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Moq;
using Xunit;

namespace Izigo.Application.Tests.Features.Auth;

/// <summary>
/// A refresh whose response is lost leaves the app holding the old token.
/// Retrying it shortly after must work, but never after logout.
/// </summary>
public class RefreshTokenGraceTests
{
    private static ITokenService Tokens()
    {
        var n = 0;
        var mock = new Mock<ITokenService>();
        mock.Setup(t => t.GenerateAccessToken(It.IsAny<User>())).Returns("access");
        mock.Setup(t => t.GenerateRefreshToken()).Returns(() => $"refresh_{++n}");
        return mock.Object;
    }

    private static async Task<(Izigo.Infrastructure.Persistence.ApplicationDbContext db, RefreshTokenHandler handler)> Setup()
    {
        var db = DbContextFactory.Create();
        var user = new User { Phone = "+2250700000099", Role = UserRole.Rider, Status = UserStatus.Active };
        db.Users.Add(user);
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id, Token = "original", ExpiresAt = DateTime.UtcNow.AddDays(30),
        });
        await db.SaveChangesAsync();
        return (db, new RefreshTokenHandler(db, Tokens()));
    }

    [Fact]
    public async Task Retry_with_just_rotated_token_succeeds_and_retires_the_lost_one()
    {
        var (db, handler) = await Setup();
        using var _ = db;

        var lost = await handler.Handle(new RefreshTokenCommand("original"), CancellationToken.None);
        var retry = await handler.Handle(new RefreshTokenCommand("original"), CancellationToken.None);

        retry.RefreshToken.Should().NotBe(lost.RefreshToken);
        db.RefreshTokens.Single(t => t.Token == lost.RefreshToken).IsRevoked.Should().BeTrue();
        db.RefreshTokens.Single(t => t.Token == retry.RefreshToken).IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task Retry_after_the_grace_window_is_rejected()
    {
        var (db, handler) = await Setup();
        using var _ = db;

        await handler.Handle(new RefreshTokenCommand("original"), CancellationToken.None);
        db.RefreshTokens.Single(t => t.Token == "original").ReplacedAt =
            DateTime.UtcNow - RefreshTokenHandler.ReuseGrace - TimeSpan.FromSeconds(1);
        await db.SaveChangesAsync();

        var act = () => handler.Handle(new RefreshTokenCommand("original"), CancellationToken.None);
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Retry_after_logout_is_rejected()
    {
        var (db, handler) = await Setup();
        using var _ = db;

        await handler.Handle(new RefreshTokenCommand("original"), CancellationToken.None);
        foreach (var t in db.RefreshTokens) t.IsRevoked = true;   // what logout does
        await db.SaveChangesAsync();

        var act = () => handler.Handle(new RefreshTokenCommand("original"), CancellationToken.None);
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Token_revoked_without_rotation_is_rejected()
    {
        var (db, handler) = await Setup();
        using var _ = db;

        db.RefreshTokens.Single().IsRevoked = true;
        await db.SaveChangesAsync();

        var act = () => handler.Handle(new RefreshTokenCommand("original"), CancellationToken.None);
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
