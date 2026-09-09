using System.Net;
using System.Text.Json;
using FluentAssertions;
using Izigo.Api.Tests.Helpers;
using Xunit;

namespace Izigo.Api.Tests.Admin;

[Collection("Integration")]
public class AdminMetricsIntegrationTests : IAsyncLifetime
{
    private readonly IzigoWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AdminMetricsIntegrationTests(IzigoWebApplicationFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();
        var token = await AuthHelper.GetAdminTokenAsync(_client);
        _client.SetAdminToken(token);
        _client.DefaultRequestHeaders.Add("X-Market", "ci");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task GetKpis_Returns200_WithSuccessShape()
    {
        var response = await _client.GetAsync("/api/v1/admin/metrics/kpis");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonSerializer.Deserialize<JsonElement>(
            await response.Content.ReadAsStringAsync(), JsonOpts);

        body.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GetGmvSeries_Returns200()
    {
        var response = await _client.GetAsync("/api/v1/admin/metrics/gmv-series");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetAlerts_Returns200()
    {
        var response = await _client.GetAsync("/api/v1/admin/metrics/alerts");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetKycQueue_Returns200_WithPaginatedList()
    {
        var response = await _client.GetAsync("/api/v1/admin/kyc/queue");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonSerializer.Deserialize<JsonElement>(
            await response.Content.ReadAsStringAsync(), JsonOpts);

        body.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GetSafetyIncidents_Returns200_WithPaginatedList()
    {
        var response = await _client.GetAsync("/api/v1/admin/safety/incidents");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonSerializer.Deserialize<JsonElement>(
            await response.Content.ReadAsStringAsync(), JsonOpts);

        body.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GetWallets_Returns200()
    {
        var response = await _client.GetAsync("/api/v1/admin/wallets");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetPayments_Returns200()
    {
        var response = await _client.GetAsync("/api/v1/admin/payments");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetPayouts_Returns200()
    {
        var response = await _client.GetAsync("/api/v1/admin/payouts");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetSupportTickets_Returns200()
    {
        var response = await _client.GetAsync("/api/v1/admin/support/tickets");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetZones_Returns200()
    {
        var response = await _client.GetAsync("/api/v1/admin/zones");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetFeatureFlags_Returns200()
    {
        var response = await _client.GetAsync("/api/v1/admin/config/flags");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetDrivers_Returns200()
    {
        var response = await _client.GetAsync("/api/v1/admin/drivers");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetCoupons_Returns200()
    {
        var response = await _client.GetAsync("/api/v1/admin/coupons");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
