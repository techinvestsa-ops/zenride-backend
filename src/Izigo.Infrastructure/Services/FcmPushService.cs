using System.Text;
using System.Text.Json;
using Izigo.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Izigo.Infrastructure.Services;

/// <summary>
/// Sends FCM pushes via the HTTP v1 API using a service-account bearer token.
/// Set Fcm:ProjectId and Fcm:ServiceAccountJson in appsettings / environment.
/// Gracefully degrades (log-only) when not configured.
/// </summary>
public sealed class FcmPushService(
    IConfiguration config,
    IHttpClientFactory httpFactory,
    ILogger<FcmPushService> logger) : IPushService
{
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private string ProjectId => config["Fcm:ProjectId"] ?? "";
    private bool   IsConfigured => !string.IsNullOrEmpty(ProjectId);

    // ── FCM payload contract (spec §24) ──────────────────────────────────────
    // android.notification.channel_id controls sound on locked Android phones.
    // apns.headers.apns-priority=10 is required for immediate delivery on iOS.

    public async Task SendAsync(
        string fcmToken, string title, string body, string type,
        string entityId, string? deepLink = null, string? payloadJson = null,
        bool highPriority = false, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            logger.LogDebug("[FCM] {Type} → {Token[..8]}… title={Title}", type, fcmToken[..Math.Min(8, fcmToken.Length)], title);
            return;
        }

        var channelId = ChannelIdFor(type);

        var message = new
        {
            message = new
            {
                token        = fcmToken,
                notification = new { title, body },
                data         = new Dictionary<string, string>
                {
                    ["type"]       = type,
                    ["entity_id"]  = entityId,
                    ["deep_link"]  = deepLink ?? $"izigo://{type.Replace('.', '/')}/{entityId}",
                    ["payload"]    = payloadJson ?? "{}",
                },
                android = new
                {
                    priority     = highPriority ? "high" : "normal",
                    notification = new { channel_id = channelId, sound = highPriority ? "offer" : "default" }
                },
                apns = new
                {
                    headers = new Dictionary<string, string>
                    {
                        ["apns-priority"]   = highPriority ? "10" : "5",
                        ["apns-push-type"]  = "alert",
                    }
                }
            }
        };

        await PostAsync(message, ct);
    }

    public async Task SendBatchAsync(
        IEnumerable<string> fcmTokens, string title, string body,
        string type, string? deepLink = null, CancellationToken ct = default)
    {
        if (!IsConfigured) return;

        // FCM v1 does not support multicast directly — send individually (batch via Hangfire in production)
        foreach (var token in fcmTokens)
            await SendAsync(token, title, body, type, "", deepLink, null, false, ct);
    }

    // ── FCM HTTP v1 ───────────────────────────────────────────────────────────

    private async Task PostAsync(object message, CancellationToken ct)
    {
        try
        {
            var accessToken = await GetAccessTokenAsync(ct);
            if (accessToken == null) return;

            var url     = $"https://fcm.googleapis.com/v1/projects/{ProjectId}/messages:send";
            var json    = JsonSerializer.Serialize(message, _json);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var client = httpFactory.CreateClient("fcm");
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var resp = await client.PostAsync(url, content, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct);
                logger.LogWarning("[FCM] Push failed {Status}: {Error}", resp.StatusCode, err);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[FCM] Push exception");
        }
    }

    // Minimal OAuth2 token using the service-account JSON key (RS256 JWT → token endpoint)
    private async Task<string?> GetAccessTokenAsync(CancellationToken ct)
    {
        var saJson = config["Fcm:ServiceAccountJson"];
        if (string.IsNullOrEmpty(saJson))
        {
            logger.LogDebug("[FCM] ServiceAccountJson not configured — skipping push");
            return null;
        }

        try
        {
            using var doc   = JsonDocument.Parse(saJson);
            var root        = doc.RootElement;
            var tokenUri    = root.GetProperty("token_uri").GetString()!;
            var clientEmail = root.GetProperty("client_email").GetString()!;
            var privateKey  = root.GetProperty("private_key").GetString()!;

            var now       = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var jwtHeader = Base64UrlEncode("""{"alg":"RS256","typ":"JWT"}""");
            var jwtClaims = Base64UrlEncode(JsonSerializer.Serialize(new
            {
                iss   = clientEmail,
                scope = "https://www.googleapis.com/auth/firebase.messaging",
                aud   = tokenUri,
                iat   = now,
                exp   = now + 3600,
            }));

            var unsigned  = $"{jwtHeader}.{jwtClaims}";
            var signature = SignRs256(privateKey, unsigned);
            var jwt       = $"{unsigned}.{signature}";

            var form = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:jwt-bearer"),
                new KeyValuePair<string, string>("assertion", jwt),
            });

            var client   = httpFactory.CreateClient("fcm");
            var response = await client.PostAsync(tokenUri, form, ct);
            var body     = await response.Content.ReadAsStringAsync(ct);
            using var r  = JsonDocument.Parse(body);
            return r.RootElement.GetProperty("access_token").GetString();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[FCM] Failed to get access token");
            return null;
        }
    }

    private static string Base64UrlEncode(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string SignRs256(string pemKey, string data)
    {
        var key = pemKey
            .Replace("-----BEGIN PRIVATE KEY-----", "")
            .Replace("-----END PRIVATE KEY-----", "")
            .Replace("\n", "").Replace("\r", "").Trim();

        var keyBytes = Convert.FromBase64String(key);
        using var rsa = System.Security.Cryptography.RSA.Create();
        rsa.ImportPkcs8PrivateKey(keyBytes, out _);

        var dataBytes = Encoding.UTF8.GetBytes(data);
        var sig       = rsa.SignData(dataBytes,
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.RSASignaturePadding.Pkcs1);

        return Convert.ToBase64String(sig)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    // Maps FCM event type → Android notification channel (spec §24)
    private static string ChannelIdFor(string type) => type switch
    {
        "job.offered"              => "trip_offers",
        "ride.status_changed"      => "trip_updates",
        "package.status_changed"   => "trip_updates",
        "coride.booking_updated"   => "trip_updates",
        "chat.message"             => "chat",
        "wallet.balance_changed"   => "payments",
        "notification.created"     => "promos",
        _                          => "trip_updates",
    };
}
