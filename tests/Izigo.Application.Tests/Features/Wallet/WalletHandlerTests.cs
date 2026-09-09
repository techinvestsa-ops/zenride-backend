using FluentAssertions;
using Izigo.Application.Features.Wallets.Commands;
using Izigo.Application.Features.Wallets.Dtos;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Xunit;

namespace Izigo.Application.Tests.Features.Wallet;

public class WalletHandlerTests
{
    private static Izigo.Domain.Entities.Wallet BuildWallet(string userId, long balance = 5000,
        bool locked = false) => new()
    {
        UserId     = userId,
        Balance    = balance,
        Currency   = "XOF",
        IsLocked   = locked,
        MinTopup   = 500,
        MaxBalance = 500_000
    };

    // ── TransferHandler ───────────────────────────────────────────────────────

    [Fact]
    public async Task Transfer_InsufficientBalance_ThrowsConflict()
    {
        using var db = DbContextFactory.Create();
        var senderId    = Guid.NewGuid().ToString();
        var recipientId = Guid.NewGuid().ToString();
        db.Wallets.Add(BuildWallet(senderId, balance: 100));
        db.Wallets.Add(BuildWallet(recipientId, balance: 0));
        await db.SaveChangesAsync();

        var handler = new TransferHandler(db, FakeServices.Realtime());
        var act = () => handler.Handle(
            new TransferCommand(senderId, new TransferRequest(recipientId, 1000, null, null)),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*INSUFFICIENT_BALANCE*");
    }

    [Fact]
    public async Task Transfer_LockedWallet_ThrowsConflict()
    {
        using var db = DbContextFactory.Create();
        var senderId    = Guid.NewGuid().ToString();
        var recipientId = Guid.NewGuid().ToString();
        db.Wallets.Add(BuildWallet(senderId, balance: 10000, locked: true));
        db.Wallets.Add(BuildWallet(recipientId));
        await db.SaveChangesAsync();

        var handler = new TransferHandler(db, FakeServices.Realtime());
        var act = () => handler.Handle(
            new TransferCommand(senderId, new TransferRequest(recipientId, 500, null, null)),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Wallet is locked*");
    }

    [Fact]
    public async Task Transfer_ExceedsMaxLimit_ThrowsValidationError()
    {
        using var db = DbContextFactory.Create();
        var senderId    = Guid.NewGuid().ToString();
        var recipientId = Guid.NewGuid().ToString();
        db.Wallets.Add(BuildWallet(senderId, balance: 500_000));
        db.Wallets.Add(BuildWallet(recipientId));
        await db.SaveChangesAsync();

        var handler = new TransferHandler(db, FakeServices.Realtime());
        var act = () => handler.Handle(
            new TransferCommand(senderId, new TransferRequest(recipientId, 200_001, null, null)),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Transfer exceeds maximum*");
    }

    [Fact]
    public async Task Transfer_Success_DeductsAndCreditsBalances()
    {
        using var db = DbContextFactory.Create();
        var senderId    = Guid.NewGuid().ToString();
        var recipientId = Guid.NewGuid().ToString();
        db.Wallets.Add(BuildWallet(senderId, balance: 5000));
        db.Wallets.Add(BuildWallet(recipientId, balance: 1000));
        await db.SaveChangesAsync();

        var handler = new TransferHandler(db, FakeServices.Realtime());
        var result = await handler.Handle(
            new TransferCommand(senderId, new TransferRequest(recipientId, 2000, "Gift", null)),
            CancellationToken.None);

        result.Type.Should().Be("transfer_out");
        db.Wallets.First(w => w.UserId == senderId).Balance.Should().Be(3000);
        db.Wallets.First(w => w.UserId == recipientId).Balance.Should().Be(3000);
        db.WalletTransactions.Should().HaveCount(2);
    }

    [Fact]
    public async Task Transfer_Idempotent_ReturnsSameResult_WhenKeyMatches()
    {
        using var db = DbContextFactory.Create();
        var senderId    = Guid.NewGuid().ToString();
        var recipientId = Guid.NewGuid().ToString();
        var senderWallet = BuildWallet(senderId, balance: 10000);
        db.Wallets.Add(senderWallet);
        db.Wallets.Add(BuildWallet(recipientId));
        await db.SaveChangesAsync();

        var idemKey = "idem-key-123";
        db.WalletTransactions.Add(new WalletTransaction
        {
            WalletId     = senderWallet.Id,
            Type         = WalletTransactionType.TransferOut,
            Amount       = -1000,
            BalanceAfter = 9000,
            ReferenceId  = $"transfer:{idemKey}",
            Status       = PaymentStatus.Succeeded,
            Title        = "Transfer"
        });
        await db.SaveChangesAsync();

        var handler = new TransferHandler(db, FakeServices.Realtime());
        var result = await handler.Handle(
            new TransferCommand(senderId, new TransferRequest(recipientId, 1000, null, idemKey)),
            CancellationToken.None);

        result.Status.Should().Be("succeeded");
        db.WalletTransactions.Count(t => t.ReferenceId == $"transfer:{idemKey}").Should().Be(1);
    }

    // ── WithdrawHandler ───────────────────────────────────────────────────────

    [Fact]
    public async Task Withdraw_InsufficientBalance_ThrowsConflict()
    {
        using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid().ToString();
        var wallet = BuildWallet(userId, balance: 100);
        db.Wallets.Add(wallet);
        var pm = new UserPaymentMethod { UserId = userId, Label = "Orange Money" };
        db.UserPaymentMethods.Add(pm);
        await db.SaveChangesAsync();

        var handler = new WithdrawHandler(db, FakeServices.Realtime());
        var act = () => handler.Handle(
            new WithdrawCommand(userId, new WithdrawRequest(1000, pm.Id, null)),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*INSUFFICIENT_BALANCE*");
    }

    [Fact]
    public async Task Withdraw_LockedWallet_ThrowsConflict()
    {
        using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid().ToString();
        db.Wallets.Add(BuildWallet(userId, balance: 10000, locked: true));
        var pm = new UserPaymentMethod { UserId = userId, Label = "Wave" };
        db.UserPaymentMethods.Add(pm);
        await db.SaveChangesAsync();

        var handler = new WithdrawHandler(db, FakeServices.Realtime());
        var act = () => handler.Handle(
            new WithdrawCommand(userId, new WithdrawRequest(1000, pm.Id, null)),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Wallet is locked*");
    }

    [Fact]
    public async Task Withdraw_PaymentMethodNotFound_ThrowsKeyNotFound()
    {
        using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid().ToString();
        db.Wallets.Add(BuildWallet(userId, balance: 10000));
        await db.SaveChangesAsync();

        var handler = new WithdrawHandler(db, FakeServices.Realtime());
        var act = () => handler.Handle(
            new WithdrawCommand(userId, new WithdrawRequest(1000, "nonexistent-pm", null)),
            CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*Payment method not found*");
    }

    [Fact]
    public async Task Withdraw_Success_DeductsBalance_AndCreatesTransaction()
    {
        using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid().ToString();
        db.Wallets.Add(BuildWallet(userId, balance: 5000));
        var pm = new UserPaymentMethod { UserId = userId, Label = "Orange Money" };
        db.UserPaymentMethods.Add(pm);
        await db.SaveChangesAsync();

        var handler = new WithdrawHandler(db, FakeServices.Realtime());
        var result = await handler.Handle(
            new WithdrawCommand(userId, new WithdrawRequest(2000, pm.Id, null)),
            CancellationToken.None);

        result.Type.Should().Be("withdrawal");
        result.Status.Should().Be("processing");
        db.Wallets.First(w => w.UserId == userId).Balance.Should().Be(3000);
        db.WalletTransactions.Should().HaveCount(1);
    }
}
