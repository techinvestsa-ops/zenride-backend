using System.Text.Json;
using Izigo.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Izigo.Infrastructure.Services;

/// <summary>
/// MTN Guinea ProSMS gateway — GET https://prosmsmtngn.com/restsms/httpget
/// </summary>
public class ProSmsMtnSmsService(
    IHttpClientFactory httpFactory,
    IConfiguration config,
    ILogger<ProSmsMtnSmsService> logger) : ISmsService
{
    private static readonly HashSet<string> SuccessStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "MESSAGE_ACCEPTED",
        "DELIVERED_TO_OPERATOR",
        "DELIVERED_TO_HANDSET",
    };

    public async Task SendAsync(string to, string message, CancellationToken ct = default)
    {
        var apiUser  = config["Sms:ApiUser"];
        var apiKey   = config["Sms:ApiKey"];
        var userId   = config["Sms:UserId"] ?? "techInvest";
        var senderId = config["Sms:SenderId"] ?? "ZENPay";
        var baseUrl  = config["Sms:BaseUrl"] ?? "https://prosmsmtngn.com/restsms/httpget";

        if (string.IsNullOrWhiteSpace(apiUser) || string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("[SMS] ProSMS MTN not configured — skipping send to {To}", to);
            logger.LogInformation("[SMS:DEV] To={To} Message={Message}", to, message);
            return;
        }

        var phone = to.Trim();
        if (!phone.StartsWith('+'))
            phone = $"+{phone.TrimStart('+')}";

        var query = string.Join("&", new[]
        {
            $"api_user={Uri.EscapeDataString(apiUser)}",
            $"api_key={Uri.EscapeDataString(apiKey)}",
            $"userid={Uri.EscapeDataString(userId)}",
            $"senderid={Uri.EscapeDataString(senderId)}",
            $"sms={Uri.EscapeDataString(message)}",
            $"phones={Uri.EscapeDataString(phone)}",
        });

        var url  = $"{baseUrl.TrimEnd('/')}?{query}";
        var client = httpFactory.CreateClient("sms");

        try
        {
            var response = await client.GetAsync(url, ct);
            var body     = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("[SMS] ProSMS HTTP {Status} body={Body}", (int)response.StatusCode, body);
                throw new HttpRequestException($"ProSMS returned {(int)response.StatusCode}: {body}");
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var errNode) &&
                errNode.ValueKind != JsonValueKind.Null &&
                errNode.ValueKind != JsonValueKind.Undefined)
            {
                var err = errNode.ToString();
                if (!string.IsNullOrWhiteSpace(err) && err != "null")
                {
                    logger.LogError("[SMS] ProSMS error={Error} to={To}", err, to);
                    throw new HttpRequestException($"ProSMS error: {err}");
                }
            }

            if (root.TryGetProperty("messages", out var messages) &&
                messages.ValueKind == JsonValueKind.Array &&
                messages.GetArrayLength() > 0)
            {
                var first = messages[0];
                var status = first.TryGetProperty("sms_status", out var st)
                    ? st.GetString() ?? ""
                    : "";

                if (!SuccessStatuses.Contains(status))
                {
                    logger.LogError("[SMS] ProSMS status={Status} body={Body}", status, body);
                    throw new HttpRequestException($"ProSMS status: {status}");
                }

                logger.LogInformation("[SMS] Sent via ProSMS MTN to={To} status={Status}", to, status);
                return;
            }

            logger.LogWarning("[SMS] ProSMS unexpected response body={Body}", body);
            throw new HttpRequestException("ProSMS returned an unexpected response.");
        }
        catch (Exception ex) when (ex is not HttpRequestException)
        {
            logger.LogError(ex, "[SMS] ProSMS failed to={To}", to);
            throw;
        }
    }
}
