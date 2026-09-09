using System.Net;
using System.Text.Json;
using FluentAssertions;
using Izigo.Api.Tests.Helpers;
using Xunit;

namespace Izigo.Api.Tests.Admin;

[Collection("Integration")]
public class AdminTripsIntegrationTests : IAsyncLifetime
{
    private readonly IzigoWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AdminTripsIntegrationTests(IzigoWebApplicationFactory factory)
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
    public async Task GetTrips_Returns200_WithPaginatedList()
    {
        var response = await _client.GetAsync("/api/v1/admin/trips");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonSerializer.Deserialize<JsonElement>(
            await response.Content.ReadAsStringAsync(), JsonOpts);

        body.GetProperty("success").GetBoolean().Should().BeTrue();
        body.TryGetProperty("meta", out var meta).Should().BeTrue();
        meta.TryGetProperty("total", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetTrips_WithFilters_Returns200()
    {
        var response = await _client.GetAsync(
            "/api/v1/admin/trips?state=completed&page=1&per_page=25");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetTrip_NonExistentId_Returns404()
    {
        var response = await _client.GetAsync("/api/v1/admin/trips/nonexistent-trip-id");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetLiveOps_Returns200()
    {
        var response = await _client.GetAsync("/api/v1/admin/ops/live");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetAdminConfig_Returns200_WithDispatchAndFlags()
    {
        var response = await _client.GetAsync("/api/v1/admin/config");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonSerializer.Deserialize<JsonElement>(
            await response.Content.ReadAsStringAsync(), JsonOpts);

        body.GetProperty("success").GetBoolean().Should().BeTrue();
        body.GetProperty("data").TryGetProperty("dispatch", out _).Should().BeTrue();
        body.GetProperty("data").TryGetProperty("flags", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetAuditLog_Returns200_WithPaginatedEntries()
    {
        var response = await _client.GetAsync("/api/v1/admin/audit-log");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonSerializer.Deserialize<JsonElement>(
            await response.Content.ReadAsStringAsync(), JsonOpts);

        body.GetProperty("success").GetBoolean().Should().BeTrue();
    }
}
