using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Izigo.Api.Tests.Helpers;
using Xunit;

namespace Izigo.Api.Tests.Admin;

[Collection("Integration")]
public class AdminStaffIntegrationTests : IAsyncLifetime
{
    private readonly IzigoWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AdminStaffIntegrationTests(IzigoWebApplicationFactory factory)
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
    public async Task GetStaff_Returns200WithSuperAdmin()
    {
        var response = await _client.GetAsync("/api/v1/admin/staff");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = JsonSerializer.Deserialize<JsonElement>(
            await response.Content.ReadAsStringAsync(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        body.GetProperty("success").GetBoolean().Should().BeTrue();
        body.GetProperty("data").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetRoles_Returns200WithAllRoles()
    {
        var response = await _client.GetAsync("/api/v1/admin/roles");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = JsonSerializer.Deserialize<JsonElement>(
            await response.Content.ReadAsStringAsync(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var roles = body.GetProperty("data").EnumerateArray().ToList();
        roles.Should().HaveCountGreaterThanOrEqualTo(7);
        roles.Should().Contain(r => r.GetProperty("key").GetString() == "super_admin");
    }

    [Fact]
    public async Task GetPermissions_Returns200WithFullCatalogue()
    {
        var response = await _client.GetAsync("/api/v1/admin/permissions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = JsonSerializer.Deserialize<JsonElement>(
            await response.Content.ReadAsStringAsync(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var perms = body.GetProperty("data").GetArrayLength();
        perms.Should().BeGreaterThanOrEqualTo(40);
    }

    [Fact]
    public async Task SuspendLastSuperAdmin_Returns409()
    {
        // Get the super admin id
        var staffResp = await _client.GetAsync("/api/v1/admin/staff");
        var staffBody = JsonSerializer.Deserialize<JsonElement>(
            await staffResp.Content.ReadAsStringAsync(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var superAdminId = staffBody.GetProperty("data")
            .EnumerateArray()
            .First(s => s.GetProperty("role_key").GetString() == "super_admin")
            .GetProperty("id").GetString();

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/admin/staff/{superAdminId}/suspend",
            new { reason = "test suspension" });

        // Should be 403 CANNOT_TARGET_SELF (suspending yourself) or 409 LAST_SUPER_ADMIN
        // Either is correct — the important thing is it does NOT succeed
        ((int)response.StatusCode).Should().BeOneOf(403, 409);
    }
}
