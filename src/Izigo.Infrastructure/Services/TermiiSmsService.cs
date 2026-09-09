using System.Net.Http.Json;
using System.Text.Json;
using Izigo.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Izigo.Infrastructure.Services;

public class TermiiSmsService(
    IHttpClientFactory httpFactory,
    IConfiguration config,
    ILogger<TermiiSmsService> logger) : ISmsService
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web);

    public async Task SendAsync(string to, string message, CancellationToken ct = default)
    {
        var apiKey   = config["Sms:ApiKey"];
        var senderId = config["Sms:SenderId"] ?? "Zenride";
        var channel  = config["Sms:Channel"]  ?? "generic";

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("[SMS] Termii not configured — skipping send to {To}", to);
            logger.LogInformation("[SMS:DEV] To={To} Message={Message}", to, message);
            return;
        }

        var payload = new
        {
            to,
            from    = senderId,
            sms     = message,
            type    = "plain",
            api_key = apiKey,
            channel
        };

        var client = httpFactory.CreateClient("sms");

        try
        {
            var response = await client.PostAsJsonAsync(
                "https://api.ng.termii.com/api/sms/send", payload, JsonOpts, ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                logger.LogError("[SMS] Termii error status={Status} body={Body}",
                    (int)response.StatusCode, error);
                throw new HttpRequestException(
                    $"Termii returned {(int)response.StatusCode}: {error}");
            }

            logger.LogInformation("[SMS] Sent via Termii to={To}", to);
        }
        catch (Exception ex) when (ex is not HttpRequestException)
        {
            logger.LogError(ex, "[SMS] Failed to send to={To}", to);
            throw;
        }
    }
}
