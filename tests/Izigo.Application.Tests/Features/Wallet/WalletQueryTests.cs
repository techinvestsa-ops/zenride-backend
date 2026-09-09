using FluentAssertions;
using Izigo.Application.Features.Wallets.Dtos;
using Izigo.Application.Features.Wallets.Queries;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Enums;
using Xunit;
using WalletEntity = Izigo.Domain.Entities.Wallet;
using WalletTx = Izigo.Domain.Entities.WalletTransaction;
using UserEntity = Izigo.Domain.Entities.User;

namespace Izigo.Application.Tests.Features.Wallet;

public class WalletQueryTests
{
    // ── GetWalletHandler ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetWallet_NoWalletExists_AutoCreatesAndReturns()
    {
        using var db = DbContextFactory.Create();
        var userId  = Guid.NewGuid().ToString();
        var handler = new GetWalletHandler(db);

        var result = await handler.Handle(new GetWalletQuery(userId), CancellationToken.None);

        result.Balance.Should().Be(0);
        result.Currency.Should().NotBeNullOrEmpty();
        db.Wallets.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetWallet_ExistingWallet_ReturnsCorrectBalance()
    {
        using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid().ToString();
        db.Wallets.Add(new WalletEntity
        {
            UserId = userId, Balance = 7500, Currency = "XOF",
            MinTopup = 500, MaxBalance = 500_000
        });
        await db.SaveChangesAsync();

        var handler = new GetWalletHandler(db);
        var result  = await handler.Handle(new GetWalletQuery(userId), CancellationToken.None);

        result.Balance.Should().Be(7500);
        result.Currency.Should().Be("XOF");
        result.IsLocked.Should().BeFalse();
    }

    [Fact]
    public async Task GetWallet_LockedWallet_ReturnsLockedState()
    {
        using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid().ToString();
        db.Wallets.Add(new WalletEntity
        {
            UserId = userId, Balance = 1000, Currency = "XOF",
            IsLocked = true, LockReason = "Suspicious activity",
            MinTopup = 500, MaxBalance = 500_000
        });
        await db.SaveChangesAsync();

        var handler = new GetWalletHandler(db);
        var result  = await handler.Handle(new GetWalletQuery(userId), CancellationToken.None);

        result.IsLocked.Should().BeTrue();
        result.LockReason.Should().Be("Suspicious activity");
    }

    // ── GetWalletTransactionsHandler ──────────────────────────────────────────

    [Fact]
    public async Task GetWalletTransactions_EmptyWallet_ReturnsEmptyList()
    {
        using var db = DbContextFactory.Create();
        var userId  = Guid.NewGuid().ToString();
        var handler = new GetWalletTransactionsHandler(db);

        var (items, total) = await handler.Handle(
            new GetWalletTransactionsQuery(userId, null, null, null, 1, 20),
            CancellationToken.None);

        items.Should().BeEmpty();
        total.Should().Be(0);
    }

    [Fact]
    public async Task GetWalletTransactions_ReturnsAllTransactions()
    {
        using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid().ToString();
        var wallet = new WalletEntity
        {
            UserId = userId, Balance = 5000, Currency = "XOF",
            MinTopup = 500, MaxBalance = 500_000
        };
        db.Wallets.Add(wallet);
        db.WalletTransactions.Add(new WalletTx
        {
            WalletId = wallet.Id, Type = WalletTransactionType.Topup,
            Amount = 2000, BalanceAfter = 5000, Title = "Top up",
            Status = PaymentStatus.Succeeded
        });
        db.WalletTransactions.Add(new WalletTx
        {
            WalletId = wallet.Id, Type = WalletTransactionType.Trip,
            Amount = -1500, BalanceAfter = 3500, Title = "Ride",
            Status = PaymentStatus.Succeeded
        });
        await db.SaveChangesAsync();

        var handler = new GetWalletTransactionsHandler(db);
        var (items, total) = await handler.Handle(
            new GetWalletTransactionsQuery(userId, null, null, null, 1, 20),
            CancellationToken.None);

        items.Should().HaveCount(2);
        total.Should().Be(2);
    }

    [Fact]
    public async Task GetWalletTransactions_FilterByType_ReturnsMatchingOnly()
    {
        using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid().ToString();
        var wallet = new WalletEntity
        {
            UserId = userId, Balance = 5000, Currency = "XOF",
            MinTopup = 500, MaxBalance = 500_000
        };
        db.Wallets.Add(wallet);
        db.WalletTransactions.AddRange(
            new WalletTx
            {
                WalletId = wallet.Id, Type = WalletTransactionType.Topup,
                Amount = 2000, BalanceAfter = 5000, Title = "Top up",
                Status = PaymentStatus.Succeeded
            },
            new WalletTx
            {
                WalletId = wallet.Id, Type = WalletTransactionType.Trip,
                Amount = -500, BalanceAfter = 4500, Title = "Ride",
                Status = PaymentStatus.Succeeded
            });
        await db.SaveChangesAsync();

        var handler = new GetWalletTransactionsHandler(db);
        var (items, total) = await handler.Handle(
            new GetWalletTransactionsQuery(userId, "topup", null, null, 1, 20),
            CancellationToken.None);

        items.Should().HaveCount(1);
        items[0].Type.Should().Be("topup");
        total.Should().Be(1);
    }

    [Fact]
    public async Task GetWalletTransactions_Pagination_RespectsPerPage()
    {
        using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid().ToString();
        var wallet = new WalletEntity
        {
            UserId = userId, Balance = 5000, Currency = "XOF",
            MinTopup = 500, MaxBalance = 500_000
        };
        db.Wallets.Add(wallet);
        for (var i = 0; i < 6; i++)
            db.WalletTransactions.Add(new WalletTx
            {
                WalletId = wallet.Id, Type = WalletTransactionType.Topup,
                Amount = 100, BalanceAfter = 100 * (i + 1), Title = $"Top up {i}",
                Status = PaymentStatus.Succeeded
            });
        await db.SaveChangesAsync();

        var handler = new GetWalletTransactionsHandler(db);
        var (items, total) = await handler.Handle(
            new GetWalletTransactionsQuery(userId, null, null, null, 1, 4),
            CancellationToken.None);

        items.Should().HaveCount(4);
        total.Should().Be(6);
    }

    // ── GetWalletLimitsHandler ────────────────────────────────────────────────

    [Fact]
    public async Task GetWalletLimits_ReturnsCorrectLimits()
    {
        using var db = DbContextFactory.Create();
        var userId  = Guid.NewGuid().ToString();
        var handler = new GetWalletLimitsHandler(db);

        var result = await handler.Handle(
            new GetWalletLimitsQuery(userId), CancellationToken.None);

        result.MinTopup.Should().BeGreaterThan(0);
        result.MaxBalance.Should().BeGreaterThan(0);
        result.MaxTransfer.Should().BeGreaterThan(0);
        result.MaxWithdrawal.Should().BeGreaterThan(0);
        result.Currency.Should().NotBeNullOrEmpty();
    }

    // ── LookupTransferRecipientHandler ────────────────────────────────────────

    [Fact]
    public async Task LookupTransferRecipient_PhoneNotFound_ThrowsKeyNotFound()
    {
        using var db = DbContextFactory.Create();
        var handler = new LookupTransferRecipientHandler(db);

        var act = () => handler.Handle(
            new LookupTransferRecipientQuery("sender1",
                new TransferLookupRequest("+2250799999999")),
            CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*phone number*");
    }

    [Fact]
    public async Task LookupTransferRecipient_SameUser_ThrowsArgumentException()
    {
        using var db = DbContextFactory.Create();
        var user = new UserEntity
        {
            FirstName = "Self", LastName = "User",
            Phone = "+2250700000001", Role = UserRole.Rider, Rating = 5.0m
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var handler = new LookupTransferRecipientHandler(db);
        var act = () => handler.Handle(
            new LookupTransferRecipientQuery(user.Id,
                new TransferLookupRequest("+2250700000001")),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*yourself*");
    }

    [Fact]
    public async Task LookupTransferRecipient_Found_ReturnsRecipientDto()
    {
        using var db = DbContextFactory.Create();
        var sender = new UserEntity
        {
            FirstName = "Sender", LastName = "A",
            Phone = "+2250700000001", Role = UserRole.Rider, Rating = 5.0m
        };
        var recipient = new UserEntity
        {
            FirstName = "Alice", LastName = "B",
            Phone = "+2250700000002", Role = UserRole.Rider, Rating = 5.0m
        };
        db.Users.AddRange(sender, recipient);
        await db.SaveChangesAsync();

        var handler = new LookupTransferRecipientHandler(db);
        var result = await handler.Handle(
            new LookupTransferRecipientQuery(sender.Id,
                new TransferLookupRequest("+2250700000002")),
            CancellationToken.None);

        result.UserId.Should().Be(recipient.Id);
        result.Name.Should().Contain("Alice");
        result.Phone.Should().Be("+2250700000002");
    }
}
