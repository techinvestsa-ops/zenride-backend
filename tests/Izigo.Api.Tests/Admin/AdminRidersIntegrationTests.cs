using System.Net;
using System.Text.Json;
using FluentAssertions;
using Izigo.Api.Tests.Helpers;
using Xunit;

namespace Izigo.Api.Tests.Admin;

[Collection("Integration")]
public class AdminRidersIntegrationTests : IAsyncLifetime
{
    private readonly IzigoWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AdminRidersIntegrationTests(IzigoWebApplicationFactory factory)
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
    public async Task GetRiders_Returns200_WithPaginatedList()
    {
        var response = await _client.GetAsync("/api/v1/admin/riders");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonSerializer.Deserialize<JsonElement>(
            await response.Content.ReadAsStringAsync(), JsonOpts);

        body.GetProperty("success").GetBoolean().Should().BeTrue();
        body.TryGetProperty("data", out _).Should().BeTrue();
        body.TryGetProperty("meta", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetRiders_WithSearchQuery_Returns200()
    {
        var response = await _client.GetAsync("/api/v1/admin/riders?q=test&page=1&per_page=10");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetRider_NonExistentId_Returns404()
    {
        var response = await _client.GetAsync("/api/v1/admin/riders/nonexistent-rider-id");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetRiders_WithStatusFilter_Returns200()
    {
        var response = await _client.GetAsync("/api/v1/admin/riders?status=active");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetRiderTrips_NonExistentRider_Returns404OrEmpty()
    {
        // Handler may throw KeyNotFoundException (→ 404) or return empty list
        var response = await _client.GetAsync("/api/v1/admin/riders/nonexistent/trips");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetRiderWallet_NonExistentRider_Returns404OrOk()
    {
        var response = await _client.GetAsync("/api/v1/admin/riders/nonexistent/wallet");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.OK);
    }
}
