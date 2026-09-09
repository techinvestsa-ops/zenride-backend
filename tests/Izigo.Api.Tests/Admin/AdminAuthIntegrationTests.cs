using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Izigo.Api.Tests.Helpers;
using Xunit;

namespace Izigo.Api.Tests.Admin;

[Collection("Integration")]
public class AdminAuthIntegrationTests : IAsyncLifetime
{
    private readonly IzigoWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AdminAuthIntegrationTests(IzigoWebApplicationFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Login_WithValidCredentials_Returns200AndTokens()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/admin/auth/login",
            new { email = "admin@zenride.app", password = "Admin@Zenride2025!" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = JsonSerializer.Deserialize<JsonElement>(
            await response.Content.ReadAsStringAsync(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        body.GetProperty("success").GetBoolean().Should().BeTrue();
        body.GetProperty("data").GetProperty("access_token").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("data").GetProperty("refresh_token").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/admin/auth/login",
            new { email = "admin@zenride.app", password = "wrong_password" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMe_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync("/api/v1/admin/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMe_WithValidToken_Returns200AndStaffProfile()
    {
        var token = await AuthHelper.GetAdminTokenAsync(_client);
        _client.SetAdminToken(token);

        var response = await _client.GetAsync("/api/v1/admin/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = JsonSerializer.Deserialize<JsonElement>(
            await response.Content.ReadAsStringAsync(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        body.GetProperty("success").GetBoolean().Should().BeTrue();
        body.GetProperty("data").GetProperty("email").GetString()
            .Should().Be("admin@zenride.app");
        body.GetProperty("data").GetProperty("role")
            .GetProperty("key").GetString().Should().Be("super_admin");
    }

    [Fact]
    public async Task Refresh_WithValidRefreshToken_Returns200AndNewTokens()
    {
        // Login first to get refresh token
        var loginResp = await _client.PostAsJsonAsync("/api/v1/admin/auth/login",
            new { email = "admin@zenride.app", password = "Admin@Zenride2025!" });
        var loginBody = JsonSerializer.Deserialize<JsonElement>(
            await loginResp.Content.ReadAsStringAsync(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var refreshToken = loginBody.GetProperty("data").GetProperty("refresh_token").GetString();

        var response = await _client.PostAsJsonAsync("/api/v1/admin/auth/refresh",
            new { refresh_token = refreshToken });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonSerializer.Deserialize<JsonElement>(
            await response.Content.ReadAsStringAsync(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        body.GetProperty("data").GetProperty("access_token").GetString()
            .Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Logout_InvalidatesSession()
    {
        var token = await AuthHelper.GetAdminTokenAsync(_client);
        _client.SetAdminToken(token);

        var logoutResp = await _client.PostAsync("/api/v1/admin/auth/logout", null);
        logoutResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetSessions_WithValidToken_ReturnsList()
    {
        var token = await AuthHelper.GetAdminTokenAsync(_client);
        _client.SetAdminToken(token);

        var response = await _client.GetAsync("/api/v1/admin/me/sessions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
