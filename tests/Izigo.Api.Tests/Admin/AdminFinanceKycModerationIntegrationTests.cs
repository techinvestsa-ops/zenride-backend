using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Izigo.Api.Tests.Helpers;
using Xunit;

namespace Izigo.Api.Tests.Admin;

/// <summary>
/// Auth enforcement and basic shape verification for Finance, KYC, and Moderation admin endpoints.
/// </summary>
[Collection("Integration")]
public class AdminFinanceKycModerationIntegrationTests : IAsyncLifetime
{
    private readonly IzigoWebApplicationFactory _factory;
    private readonly HttpClient _unauthClient;
    private readonly HttpClient _adminClient;

    public AdminFinanceKycModerationIntegrationTests(IzigoWebApplicationFactory factory)
    {
        _factory      = factory;
        _unauthClient = factory.CreateClient();
        _adminClient  = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();
        var token = await AuthHelper.GetAdminTokenAsync(_adminClient);
        _adminClient.SetAdminToken(token);
        _adminClient.DefaultRequestHeaders.Add("X-Market", "ci");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Finance — Payments ────────────────────────────────────────────────────

    [Fact]
    public async Task GetPayments_WithoutToken_Returns401()
        => (await _unauthClient.GetAsync("/api/v1/admin/payments")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task GetPayments_WithAdminToken_Returns200()
    {
        var r = await _adminClient.GetAsync("/api/v1/admin/payments");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetPaymentStats_WithoutToken_Returns401()
        => (await _unauthClient.GetAsync("/api/v1/admin/payments/stats")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    // ── Finance — Wallets ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetWallets_WithoutToken_Returns401()
        => (await _unauthClient.GetAsync("/api/v1/admin/wallets")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task GetWallets_WithAdminToken_Returns200()
    {
        var r = await _adminClient.GetAsync("/api/v1/admin/wallets");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetWalletTotals_WithAdminToken_Returns200()
    {
        var r = await _adminClient.GetAsync("/api/v1/admin/wallets/totals");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Finance — Payouts ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetPayouts_WithoutToken_Returns401()
        => (await _unauthClient.GetAsync("/api/v1/admin/payouts")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task GetPayouts_WithAdminToken_Returns200()
    {
        var r = await _adminClient.GetAsync("/api/v1/admin/payouts");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ApprovePayout_NonExistent_Returns404OrConflict()
    {
        var r = await _adminClient.PostAsJsonAsync(
            "/api/v1/admin/payouts/nonexistent-id/approve", new { reason = "approved" });
        ((int)r.StatusCode).Should().BeOneOf(404, 409, 422);
    }

    // ── Finance — Pricing ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetFareRules_WithoutToken_Returns401()
        => (await _unauthClient.GetAsync("/api/v1/admin/pricing/fare-rules")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task GetFareRules_WithAdminToken_Returns200()
    {
        var r = await _adminClient.GetAsync("/api/v1/admin/pricing/fare-rules");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetCommission_WithAdminToken_Returns200()
    {
        var r = await _adminClient.GetAsync("/api/v1/admin/pricing/commission");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetSurge_WithAdminToken_Returns200()
    {
        var r = await _adminClient.GetAsync("/api/v1/admin/pricing/surge");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── KYC ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetKycQueue_WithoutToken_Returns401()
        => (await _unauthClient.GetAsync("/api/v1/admin/kyc/queue")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task GetKycQueue_WithAdminToken_Returns200()
    {
        var r = await _adminClient.GetAsync("/api/v1/admin/kyc/queue");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetKycMetrics_WithAdminToken_Returns200()
    {
        var r = await _adminClient.GetAsync("/api/v1/admin/kyc/metrics");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetKycDetail_NonExistentDriver_Returns404()
    {
        var r = await _adminClient.GetAsync("/api/v1/admin/kyc/nonexistent-driver");
        r.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ApproveKycStep_WithoutToken_Returns401()
        => (await _unauthClient.PostAsJsonAsync(
            "/api/v1/admin/kyc/driver1/steps/identity/approve", new { })).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task RejectKycStep_WithoutToken_Returns401()
        => (await _unauthClient.PostAsJsonAsync(
            "/api/v1/admin/kyc/driver1/steps/identity/reject", new { })).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    // ── Moderation — Drivers ──────────────────────────────────────────────────

    [Fact]
    public async Task GetDrivers_WithoutToken_Returns401()
        => (await _unauthClient.GetAsync("/api/v1/admin/drivers")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task GetDrivers_WithAdminToken_Returns200()
    {
        var r = await _adminClient.GetAsync("/api/v1/admin/drivers");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetDriver_NonExistent_Returns404()
    {
        var r = await _adminClient.GetAsync("/api/v1/admin/drivers/nonexistent-driver");
        r.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Moderation — Riders ───────────────────────────────────────────────────

    [Fact]
    public async Task SuspendRider_WithoutToken_Returns401()
        => (await _unauthClient.PostAsJsonAsync(
            "/api/v1/admin/riders/fake-id/suspend", new { })).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task BlockRider_WithoutToken_Returns401()
        => (await _unauthClient.PostAsJsonAsync(
            "/api/v1/admin/riders/fake-id/block", new { })).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task FlagRider_WithoutToken_Returns401()
        => (await _unauthClient.PostAsJsonAsync(
            "/api/v1/admin/riders/fake-id/flag", new { })).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    // ── Safety ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSosIncidents_WithoutToken_Returns401()
        => (await _unauthClient.GetAsync("/api/v1/admin/safety/incidents")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task GetSosIncidents_WithAdminToken_Returns200()
    {
        var r = await _adminClient.GetAsync("/api/v1/admin/safety/incidents");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Growth — Broadcasts ───────────────────────────────────────────────────

    [Fact]
    public async Task GetBroadcasts_WithoutToken_Returns401()
        => (await _unauthClient.GetAsync("/api/v1/admin/broadcasts")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task GetBroadcasts_WithAdminToken_Returns200()
    {
        var r = await _adminClient.GetAsync("/api/v1/admin/broadcasts");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Platform — Zones ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetZones_WithoutToken_Returns401()
        => (await _unauthClient.GetAsync("/api/v1/admin/zones")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task GetZones_WithAdminToken_Returns200()
    {
        var r = await _adminClient.GetAsync("/api/v1/admin/zones");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
