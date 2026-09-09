using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Izigo.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Izigo.Infrastructure.Services;

/// <summary>
/// Routes payments to CinetPay (mobile money + card) or Wave based on method.
/// When credentials are absent the service logs a warning and returns stub responses
/// so the rest of the flow (DB records, idempotency, webhook handling) still works in dev.
/// Swap-ready: the interface is kept minimal so an alternative gateway can replace this
/// class without touching any handler.
/// </summary>
public class PaymentGatewayService(
    IHttpClientFactory http,
    IConfiguration config,
    ILogger<PaymentGatewayService> logger) : IPaymentGateway
{
    private static readonly JsonSerializerOptions _opts =
        new(JsonSerializerDefaults.Web);

    // ── Public interface ──────────────────────────────────────────────────────

    public async Task<PaymentInitResult> InitiateAsync(
        string paymentId, long amount, string currency,
        string method, string? phone, string? returnUrl,
        CancellationToken ct = default)
    {
        return method switch
        {
            "cash" or "wallet" =>
                new PaymentInitResult(paymentId, "succeeded",
                    null, null, null, false),

            "wave" =>
                await InitiateWaveAsync(paymentId, amount, currency, ct),

            _ =>   // orange_money | moov_money | mtn_momo | card
                await InitiateCinetPayAsync(paymentId, amount, currency, method, phone, ct)
        };
    }

    public bool VerifyWebhookSignature(string gateway, string rawPayload, string? signature)
    {
        return gateway.ToLower() switch
        {
            "wave"      => VerifyWaveSignature(rawPayload, signature),
            "cinetpay"  => VerifyCinetPaySignature(rawPayload, signature),
            _           => true   // unknown gateway — accept and let handler decide
        };
    }

    // ── CinetPay ─────────────────────────────────────────────────────────────

    private async Task<PaymentInitResult> InitiateCinetPayAsync(
        string paymentId, long amount, string currency,
        string method, string? phone, CancellationToken ct)
    {
        var apiKey    = config["PaymentGateway:CinetPay:ApiKey"];
        var siteId    = config["PaymentGateway:CinetPay:SiteId"];
        var notifyUrl = config["PaymentGateway:CinetPay:NotifyUrl"]
                        ?? "https://api.izigo.app/api/v1/webhooks/cinetpay";
        var returnUrl = config["PaymentGateway:CinetPay:ReturnUrl"]
                        ?? "https://app.izigo.app/payment/callback";

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(siteId))
        {
            logger.LogWarning(
                "[Payment:DEV] CinetPay not configured — stub response for paymentId={Id} method={Method}",
                paymentId, method);

            return method == "card"
                ? new PaymentInitResult(paymentId, "pending",
                    CheckoutUrl: $"https://secure.cinetpay.com/payment/stub/{paymentId}",
                    null, null, false)
                : new PaymentInitResult(paymentId, "processing",
                    null, UssdCode: $"#144*4*6*{paymentId[^6..]}#", null, false);
        }

        // CinetPay uses "MOBILE_MONEY" for all mobile channels; "ALL" enables card too
        var channels = method == "card" ? "ALL" : "MOBILE_MONEY";

        var body = new
        {
            apikey         = apiKey,
            site_id        = siteId,
            transaction_id = paymentId,
            amount,
            currency,
            description    = "Izigo payment",
            return_url     = returnUrl,
            notify_url     = notifyUrl,
            channels,
            // Pass phone for mobile money pre-fill (optional — CinetPay shows it on their page)
            customer_phone_number = phone
        };

        try
        {
            var client   = http.CreateClient("payment");
            var response = await client.PostAsJsonAsync(
                "https://api-checkout.cinetpay.com/v2/payment", body, _opts, ct);

            var raw = await response.Content.ReadAsStringAsync(ct);
            logger.LogDebug("[CinetPay] raw response: {Raw}", raw);

            using var doc  = JsonDocument.Parse(raw);
            var code       = doc.RootElement.GetProperty("code").GetString();

            if (code != "201")
            {
                var msg = doc.RootElement.TryGetProperty("message", out var m)
                    ? m.GetString() : "Unknown error";
                logger.LogError("[CinetPay] initiation failed: {Code} {Msg}", code, msg);
                throw new InvalidOperationException($"Payment gateway error: {msg}");
            }

            var data       = doc.RootElement.GetProperty("data");
            var paymentUrl = data.TryGetProperty("payment_url", out var u) ? u.GetString() : null;

            return method == "card"
                ? new PaymentInitResult(paymentId, "pending", paymentUrl, null, null, false)
                : new PaymentInitResult(paymentId, "processing", null, null,
                    DeepLink: paymentUrl, false);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            logger.LogError(ex, "[CinetPay] HTTP error for paymentId={Id}", paymentId);
            throw new InvalidOperationException("Payment gateway unreachable. Please try again.");
        }
    }

    // CinetPay webhook verification: the gateway sends cpm_site_id in the payload.
    // We verify it matches our configured site ID — no HMAC on v2 webhooks.
    private bool VerifyCinetPaySignature(string rawPayload, string? _)
    {
        var siteId = config["PaymentGateway:CinetPay:SiteId"];
        if (string.IsNullOrWhiteSpace(siteId))
        {
            logger.LogWarning("[Payment:DEV] CinetPay site_id not configured — accepting webhook");
            return true;
        }

        try
        {
            using var doc = JsonDocument.Parse(rawPayload);
            var incoming  = doc.RootElement.TryGetProperty("cpm_site_id", out var id)
                            ? id.GetString() : null;
            var ok        = incoming == siteId;
            if (!ok)
                logger.LogWarning("[CinetPay] site_id mismatch: got={Got} expected={Expected}",
                    incoming, siteId);
            return ok;
        }
        catch
        {
            return false;
        }
    }

    // ── Wave ─────────────────────────────────────────────────────────────────

    private async Task<PaymentInitResult> InitiateWaveAsync(
        string paymentId, long amount, string currency, CancellationToken ct)
    {
        var apiKey     = config["PaymentGateway:Wave:ApiKey"];
        var successUrl = config["PaymentGateway:Wave:SuccessUrl"]
                         ?? "https://app.izigo.app/payment/callback?status=success";
        var errorUrl   = config["PaymentGateway:Wave:ErrorUrl"]
                         ?? "https://app.izigo.app/payment/callback?status=error";
        var webhookUrl = config["PaymentGateway:Wave:WebhookUrl"]
                         ?? "https://api.izigo.app/api/v1/webhooks/wave";

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning(
                "[Payment:DEV] Wave not configured — stub response for paymentId={Id}", paymentId);
            return new PaymentInitResult(paymentId, "processing",
                null, null, DeepLink: $"https://pay.wave.com/m/stub/{paymentId}", false);
        }

        // Wave amount is a string (decimal representation)
        var body = new
        {
            currency,
            amount           = amount.ToString(),
            success_url      = successUrl,
            error_url        = errorUrl,
            client_reference = paymentId,   // we'll use this to match the webhook
            webhook_url      = webhookUrl
        };

        try
        {
            var client = http.CreateClient("payment");
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            var response = await client.PostAsJsonAsync(
                "https://api.wave.com/v1/checkout/sessions", body, _opts, ct);

            var raw = await response.Content.ReadAsStringAsync(ct);
            logger.LogDebug("[Wave] raw response: {Raw}", raw);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("[Wave] initiation failed: {Status} {Raw}",
                    (int)response.StatusCode, raw);
                throw new InvalidOperationException("Wave payment gateway error. Please try again.");
            }

            using var doc       = JsonDocument.Parse(raw);
            var launchUrl       = doc.RootElement.TryGetProperty("wave_launch_url", out var u)
                                  ? u.GetString() : null;

            return new PaymentInitResult(paymentId, "processing",
                null, null, DeepLink: launchUrl, false);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            logger.LogError(ex, "[Wave] HTTP error for paymentId={Id}", paymentId);
            throw new InvalidOperationException("Payment gateway unreachable. Please try again.");
        }
    }

    // Wave webhook verification: X-Wave-Signature = "sha256=HMAC-SHA256(payload, secret)"
    private bool VerifyWaveSignature(string rawPayload, string? signature)
    {
        var secret = config["PaymentGateway:Wave:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            logger.LogWarning("[Payment:DEV] Wave webhook secret not configured — accepting webhook");
            return true;
        }

        if (string.IsNullOrWhiteSpace(signature))
            return false;

        // Strip "sha256=" prefix if present
        var sigBody = signature.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)
            ? signature[7..] : signature;

        var key      = Encoding.UTF8.GetBytes(secret);
        var payload  = Encoding.UTF8.GetBytes(rawPayload);
        var expected = Convert.ToHexString(HMACSHA256.HashData(key, payload)).ToLower();

        var ok = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(sigBody.ToLower()),
            Encoding.UTF8.GetBytes(expected));

        if (!ok)
            logger.LogWarning("[Wave] signature mismatch");
        return ok;
    }
}
