using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace Izigo.Api.Tests.App;

/// <summary>
/// 401 enforcement on all driver-facing endpoints.
/// Drivers must authenticate with AppBearer + role=driver claim.
/// </summary>
[Collection("Integration")]
public class AppDriverIntegrationTests : IAsyncLifetime
{
    private readonly IzigoWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AppDriverIntegrationTests(IzigoWebApplicationFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync()    => Task.CompletedTask;

    // ── Onboarding ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetOnboarding_WithoutToken_Returns401()
    {
        var r = await _client.GetAsync("/api/v1/driver/onboarding");
        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SubmitPersonal_WithoutToken_Returns401()
    {
        var r = await _client.PostAsJsonAsync("/api/v1/driver/onboarding/personal", new { });
        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SubmitVehicle_WithoutToken_Returns401()
    {
        var r = await _client.PostAsJsonAsync("/api/v1/driver/onboarding/vehicle", new { });
        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SubmitOnboarding_WithoutToken_Returns401()
    {
        var r = await _client.PostAsJsonAsync("/api/v1/driver/onboarding/submit", new { });
        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── KYC / Status ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetKyc_WithoutToken_Returns401()
    {
        var r = await _client.GetAsync("/api/v1/driver/kyc");
        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetDriverStatus_WithoutToken_Returns401()
    {
        var r = await _client.GetAsync("/api/v1/driver/status");
        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SetDriverStatus_WithoutToken_Returns401()
    {
        var r = await _client.PostAsJsonAsync("/api/v1/driver/status",
            new { is_online = true });
        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Job lifecycle ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetActiveJob_WithoutToken_Returns401()
    {
        var r = await _client.GetAsync("/api/v1/driver/jobs/active");
        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AcceptJob_WithoutToken_Returns401()
    {
        var r = await _client.PostAsJsonAsync("/api/v1/driver/jobs/fake-id/accept", new { });
        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeclineJob_WithoutToken_Returns401()
    {
        var r = await _client.PostAsJsonAsync("/api/v1/driver/jobs/fake-id/decline", new { });
        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EnRouteJob_WithoutToken_Returns401()
    {
        var r = await _client.PostAsJsonAsync("/api/v1/driver/jobs/fake-id/en-route", new { });
        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ArrivedPickup_WithoutToken_Returns401()
    {
        var r = await _client.PostAsJsonAsync("/api/v1/driver/jobs/fake-id/arrived-pickup", new { });
        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task StartJob_WithoutToken_Returns401()
    {
        var r = await _client.PostAsJsonAsync("/api/v1/driver/jobs/fake-id/start",
            new { otp = "1234" });
        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CompleteJob_WithoutToken_Returns401()
    {
        var r = await _client.PostAsJsonAsync("/api/v1/driver/jobs/fake-id/complete", new { });
        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CancelJob_WithoutToken_Returns401()
    {
        var r = await _client.PostAsJsonAsync("/api/v1/driver/jobs/fake-id/cancel", new { });
        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RateCustomer_WithoutToken_Returns401()
    {
        var r = await _client.PostAsJsonAsync("/api/v1/driver/jobs/fake-id/rate-customer",
            new { stars = 5 });
        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Earnings / Wallet ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetDriverEarnings_WithoutToken_Returns401()
    {
        var r = await _client.GetAsync("/api/v1/driver/earnings");
        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetDriverWallet_WithoutToken_Returns401()
    {
        var r = await _client.GetAsync("/api/v1/driver/wallet");
        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DriverWithdraw_WithoutToken_Returns401()
    {
        var r = await _client.PostAsJsonAsync("/api/v1/driver/wallet/withdraw", new { amount = 1000 });
        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Location ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task PostLocation_WithoutToken_Returns401()
    {
        var r = await _client.PostAsJsonAsync("/api/v1/driver/location",
            new { lat = 5.3, lng = -4.0 });
        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Public endpoints ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetBanks_WithoutToken_Returns401()
    {
        var r = await _client.GetAsync("/api/v1/driver/banks");
        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
