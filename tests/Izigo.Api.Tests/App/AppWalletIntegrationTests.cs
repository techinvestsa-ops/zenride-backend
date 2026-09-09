using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace Izigo.Api.Tests.App;

/// <summary>
/// Integration tests for wallet and payment endpoints.
/// All wallet operations require AppBearer authentication.
/// </summary>
[Collection("Integration")]
public class AppWalletIntegrationTests : IAsyncLifetime
{
    private readonly IzigoWebApplicationFactory _factory;
    private readonly HttpClient _unauthClient;

    public AppWalletIntegrationTests(IzigoWebApplicationFactory factory)
    {
        _factory      = factory;
        _unauthClient = factory.CreateClient();
    }

    // 401-only tests — no DB state required, skip the expensive reset
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    // ── Auth enforcement ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetWallet_WithoutToken_Returns401()
    {
        var response = await _unauthClient.GetAsync("/api/v1/wallet");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetWalletTransactions_WithoutToken_Returns401()
    {
        var response = await _unauthClient.GetAsync("/api/v1/wallet/transactions");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetWalletSummary_WithoutToken_Returns401()
    {
        var response = await _unauthClient.GetAsync("/api/v1/wallet/summary");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetWalletLimits_WithoutToken_Returns401()
    {
        var response = await _unauthClient.GetAsync("/api/v1/wallet/limits");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TopUpWallet_WithoutToken_Returns401()
    {
        var response = await _unauthClient.PostAsJsonAsync("/api/v1/wallet/topup",
            new { amount = 2000, method = "orange_money" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LookupTransferRecipient_WithoutToken_Returns401()
    {
        var response = await _unauthClient.PostAsJsonAsync("/api/v1/wallet/transfer/lookup",
            new { phone = "+2250700000001" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Transfer_WithoutToken_Returns401()
    {
        var response = await _unauthClient.PostAsJsonAsync("/api/v1/wallet/transfer",
            new { recipient_user_id = "usr_001", amount = 500 });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Withdraw_WithoutToken_Returns401()
    {
        var response = await _unauthClient.PostAsJsonAsync("/api/v1/wallet/withdraw",
            new { amount = 2000, payment_method_id = "pm_001" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetPaymentMethods_WithoutToken_Returns401()
    {
        var response = await _unauthClient.GetAsync("/api/v1/payment-methods");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeletePaymentMethod_WithoutToken_Returns401()
    {
        var response = await _unauthClient.DeleteAsync("/api/v1/payment-methods/fake-id");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Payment endpoint auth enforcement ─────────────────────────────────────

    [Fact]
    public async Task CreatePaymentIntent_WithoutToken_Returns401()
    {
        var response = await _unauthClient.PostAsJsonAsync("/api/v1/payments/intents",
            new { amount = 2000, method = "orange_money" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetPaymentStatus_WithoutToken_Returns401()
    {
        var response = await _unauthClient.GetAsync("/api/v1/payments/fake-id");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Notification auth enforcement ─────────────────────────────────────────

    [Fact]
    public async Task GetNotifications_WithoutToken_Returns401()
    {
        var response = await _unauthClient.GetAsync("/api/v1/notifications");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetNotificationUnreadCount_WithoutToken_Returns401()
    {
        var response = await _unauthClient.GetAsync("/api/v1/notifications/unread-count");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
