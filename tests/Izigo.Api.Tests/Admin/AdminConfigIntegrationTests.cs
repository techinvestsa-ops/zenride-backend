using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Izigo.Api.Tests.Helpers;
using Xunit;

namespace Izigo.Api.Tests.Admin;

[Collection("Integration")]
public class AdminConfigIntegrationTests : IAsyncLifetime
{
    private readonly IzigoWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AdminConfigIntegrationTests(IzigoWebApplicationFactory factory)
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

    [Fact]
    public async Task GetConfig_Returns200WithPlatformConfig()
    {
        var response = await _client.GetAsync("/api/v1/admin/config");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = JsonSerializer.Deserialize<JsonElement>(
            await response.Content.ReadAsStringAsync(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        body.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task GetFeatureFlags_Returns200()
    {
        var response = await _client.GetAsync("/api/v1/admin/config/flags");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetZones_Returns200()
    {
        var response = await _client.GetAsync("/api/v1/admin/zones");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetAuditLog_Returns200()
    {
        var response = await _client.GetAsync("/api/v1/admin/audit-log");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
