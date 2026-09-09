using System.Net.Http.Json;
using System.Text.Json;

namespace Izigo.Api.Tests.Helpers;

public static class AuthHelper
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Logs in as the seeded super admin and returns the access token.</summary>
    public static async Task<string> GetAdminTokenAsync(HttpClient client,
        string email = "admin@zenride.app",
        string password = "Admin@Zenride2025!")
    {
        var response = await client.PostAsJsonAsync("/api/v1/admin/auth/login",
            new { email, password });

        response.EnsureSuccessStatusCode();

        var body  = await response.Content.ReadAsStringAsync();
        var doc   = JsonSerializer.Deserialize<JsonElement>(body, JsonOpts);
        var token = doc.GetProperty("data").GetProperty("access_token").GetString()
                    ?? throw new InvalidOperationException("No access_token in login response");
        return token;
    }

    public static void SetAdminToken(this HttpClient client, string token)
        => client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
}
