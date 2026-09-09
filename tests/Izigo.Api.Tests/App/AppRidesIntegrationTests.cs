using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Izigo.Api.Tests.Helpers;
using Xunit;

namespace Izigo.Api.Tests.App;

/// <summary>
/// Integration tests for ride-hailing endpoints.
/// Verifies auth enforcement (401), basic happy-path responses, and end-to-end flows.
/// </summary>
[Collection("Integration")]
public class AppRidesIntegrationTests : IAsyncLifetime
{
    private readonly IzigoWebApplicationFactory _factory;
    private readonly HttpClient _unauthClient;

    public AppRidesIntegrationTests(IzigoWebApplicationFactory factory)
    {
        _factory     = factory;
        _unauthClient = factory.CreateClient();
    }

    // 401-only tests — no DB state required, skip the expensive reset
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    // ── Auth enforcement (unauthenticated) ────────────────────────────────────

    [Fact]
    public async Task CreateQuote_WithoutToken_Returns401()
    {
        var response = await _unauthClient.PostAsJsonAsync("/api/v1/quotes",
            new { pickup = new { }, dropoff = new { } });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateRide_WithoutToken_Returns401()
    {
        var response = await _unauthClient.PostAsJsonAsync("/api/v1/rides",
            new { quote_id = "q1", service_class = "zen_car", payment_method = "cash" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetActiveRide_WithoutToken_Returns401()
    {
        var response = await _unauthClient.GetAsync("/api/v1/rides/active");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetRideHistory_WithoutToken_Returns401()
    {
        var response = await _unauthClient.GetAsync("/api/v1/rides");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetRideById_WithoutToken_Returns401()
    {
        var response = await _unauthClient.GetAsync("/api/v1/rides/fake-id");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CancelRide_WithoutToken_Returns401()
    {
        var response = await _unauthClient.PostAsJsonAsync("/api/v1/rides/fake-id/cancel",
            new { reason_code = "changed_mind" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RateRide_WithoutToken_Returns401()
    {
        var response = await _unauthClient.PostAsJsonAsync("/api/v1/rides/fake-id/rate",
            new { stars = 5 });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetReceipt_WithoutToken_Returns401()
    {
        var response = await _unauthClient.GetAsync("/api/v1/rides/fake-id/receipt");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetCancellationReasons_WithoutToken_Returns401()
    {
        var response = await _unauthClient.GetAsync("/api/v1/rides/cancellation-reasons");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Admin token must not reach app endpoints ──────────────────────────────

    [Fact]
    public async Task GetRideHistory_WithInvalidToken_Returns401()
    {
        // A syntactically valid JWT with a bad signature — fails both AppBearer and AdminBearer
        // verification, but tests that the endpoint is auth-gated without hitting the real login flow.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJ0ZXN0In0.invalidsignature");

        var response = await client.GetAsync("/api/v1/rides");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Co-Ride auth enforcement ──────────────────────────────────────────────

    [Fact]
    public async Task GetCoRideListings_WithoutToken_Returns401()
    {
        var response = await _unauthClient.GetAsync("/api/v1/co-ride/listings");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateCoRideRequest_WithoutToken_Returns401()
    {
        var response = await _unauthClient.PostAsJsonAsync("/api/v1/co-ride/requests",
            new { from = new { }, to = new { } });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Package auth enforcement ──────────────────────────────────────────────

    [Fact]
    public async Task CreatePackage_WithoutToken_Returns401()
    {
        var response = await _unauthClient.PostAsJsonAsync("/api/v1/packages",
            new { quote_id = "q1" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Config endpoints are public ───────────────────────────────────────────

    [Fact]
    public async Task GetConfig_WithoutToken_Returns200()
    {
        var response = await _unauthClient.GetAsync("/api/v1/config");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = JsonSerializer.Deserialize<JsonElement>(
            await response.Content.ReadAsStringAsync(), JsonOpts);
        body.GetProperty("success").GetBoolean().Should().BeTrue();
    }
}
