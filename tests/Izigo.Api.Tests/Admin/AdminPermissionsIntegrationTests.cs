using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Izigo.Api.Tests.Helpers;
using Xunit;

namespace Izigo.Api.Tests.Admin;

[Collection("Integration")]
public class AdminPermissionsIntegrationTests : IAsyncLifetime
{
    private readonly IzigoWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AdminPermissionsIntegrationTests(IzigoWebApplicationFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ── Authentication enforcement ────────────────────────────────────────────

    [Theory]
    [InlineData("GET", "/api/v1/admin/me")]
    [InlineData("GET", "/api/v1/admin/staff")]
    [InlineData("GET", "/api/v1/admin/trips")]
    [InlineData("GET", "/api/v1/admin/riders")]
    [InlineData("GET", "/api/v1/admin/drivers")]
    [InlineData("GET", "/api/v1/admin/metrics/kpis")]
    [InlineData("GET", "/api/v1/admin/config")]
    [InlineData("GET", "/api/v1/admin/audit-log")]
    [InlineData("GET", "/api/v1/admin/kyc/queue")]
    [InlineData("GET", "/api/v1/admin/safety/incidents")]
    [InlineData("GET", "/api/v1/admin/wallets")]
    [InlineData("GET", "/api/v1/admin/payments")]
    [InlineData("GET", "/api/v1/admin/payouts")]
    [InlineData("GET", "/api/v1/admin/coupons")]
    [InlineData("GET", "/api/v1/admin/support/tickets")]
    [InlineData("GET", "/api/v1/admin/zones")]
    [InlineData("GET", "/api/v1/admin/roles")]
    public async Task AdminEndpoints_WithoutToken_Return401(string method, string url)
    {
        var request  = new HttpRequestMessage(new HttpMethod(method), url);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            because: $"{method} {url} should require auth");
    }

    [Fact]
    public async Task AdminEndpoints_WithAppBearerToken_Return401()
    {
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "fake-app-token");

        var response = await _client.GetAsync("/api/v1/admin/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AdminLogin_InvalidJson_Returns400()
    {
        var content  = new StringContent("{invalid json}", System.Text.Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/v1/admin/auth/login", content);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task AdminLogin_MissingFields_Returns422()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/admin/auth/login", new { email = "" });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task AdminEndpoints_WithValidToken_AcceptRequests()
    {
        var token = await AuthHelper.GetAdminTokenAsync(_client);
        _client.SetAdminToken(token);
        _client.DefaultRequestHeaders.Add("X-Market", "ci");

        var response = await _client.GetAsync("/api/v1/admin/staff");
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }
}
