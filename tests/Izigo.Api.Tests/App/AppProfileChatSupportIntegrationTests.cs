using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace Izigo.Api.Tests.App;

/// <summary>
/// 401 enforcement on profile, chat, support, safety, promo, and geo endpoints.
/// </summary>
[Collection("Integration")]
public class AppProfileChatSupportIntegrationTests : IAsyncLifetime
{
    private readonly HttpClient _client;

    public AppProfileChatSupportIntegrationTests(IzigoWebApplicationFactory factory)
        => _client = factory.CreateClient();

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync()    => Task.CompletedTask;

    // ── Profile (base route: api/v1/me) ───────────────────────────────────────

    [Fact]
    public async Task GetProfile_WithoutToken_Returns401()
        => (await _client.GetAsync("/api/v1/me")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task UpdateProfile_WithoutToken_Returns401()
        => (await _client.PatchAsJsonAsync("/api/v1/me", new { })).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task GetEmergencyContacts_WithoutToken_Returns401()
        => (await _client.GetAsync("/api/v1/me/emergency-contacts")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task AddEmergencyContact_WithoutToken_Returns401()
        => (await _client.PostAsJsonAsync("/api/v1/me/emergency-contacts", new { })).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task GetPreferences_WithoutToken_Returns401()
        => (await _client.GetAsync("/api/v1/me/preferences")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task CloseAccount_WithoutToken_Returns401()
        => (await _client.PostAsJsonAsync("/api/v1/me/close-account", new { })).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    // ── Saved Places ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSavedPlaces_WithoutToken_Returns401()
        => (await _client.GetAsync("/api/v1/places")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task AddSavedPlace_WithoutToken_Returns401()
        => (await _client.PostAsJsonAsync("/api/v1/places", new { })).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    // ── Chat (base route: api/v1/conversations) ───────────────────────────────

    [Fact]
    public async Task GetConversations_WithoutToken_Returns401()
        => (await _client.GetAsync("/api/v1/conversations")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task GetOrCreateConversation_WithoutToken_Returns401()
        => (await _client.PostAsJsonAsync("/api/v1/conversations/trip/fake-id", new { })).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task SendMessage_WithoutToken_Returns401()
        => (await _client.PostAsJsonAsync("/api/v1/conversations/fake-id/messages", new { })).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task MarkConversationRead_WithoutToken_Returns401()
        => (await _client.PostAsJsonAsync("/api/v1/conversations/fake-id/read", new { })).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    // ── Support ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTickets_WithoutToken_Returns401()
        => (await _client.GetAsync("/api/v1/support/tickets")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task CreateTicket_WithoutToken_Returns401()
        => (await _client.PostAsJsonAsync("/api/v1/support/tickets", new { })).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task ReplyTicket_WithoutToken_Returns401()
        => (await _client.PostAsJsonAsync("/api/v1/support/tickets/fake-id/reply", new { })).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task GetFaqs_WithoutToken_Returns401()
        => (await _client.GetAsync("/api/v1/support/faqs")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    // ── Safety ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RaiseSos_WithoutToken_Returns401()
        => (await _client.PostAsJsonAsync("/api/v1/safety/sos", new { })).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task ShareTripCreate_WithoutToken_Returns401()
        => (await _client.PostAsJsonAsync("/api/v1/trips/fake-id/share", new { })).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    // ── Promotions ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetCoupons_WithoutToken_Returns401()
        => (await _client.GetAsync("/api/v1/coupons")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task ApplyCoupon_WithoutToken_Returns401()
        => (await _client.PostAsJsonAsync("/api/v1/coupons/validate", new { })).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    // ── Geo ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GeoAutocomplete_WithoutToken_Returns401()
        => (await _client.GetAsync("/api/v1/geo/autocomplete?q=Abidjan")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task GeoNearbyDrivers_WithoutToken_Returns401()
        => (await _client.GetAsync("/api/v1/geo/nearby-drivers?lat=5.3&lng=-4.0")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

    // ── Realtime ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task RealtimeAuth_WithoutToken_Returns401()
        => (await _client.PostAsJsonAsync("/api/v1/realtime/auth", new { })).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
}
