using Izigo.Application.Common.Models;
using Izigo.Application.Features.Payments.Commands;
using Izigo.Application.Features.Payments.Dtos;
using Izigo.Application.Features.Payments.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Izigo.Api.Controllers.V1;

/// <summary>
/// Manages payment methods, payment intents, payment history, and inbound
/// payment-gateway webhooks (signature verification required).
/// </summary>
[Route("api/v1")]
public class PaymentsController : BaseController
{
    // ── Payment Methods ───────────────────────────────────────────────────────

    /// <summary>Returns all saved payment methods for the authenticated user.</summary>
    [Authorize(Policy = "AppPolicy")]
    [HttpGet("payment-methods")]
    public async Task<IActionResult> GetPaymentMethods()
        => Ok(await Mediator.Send(new GetPaymentMethodsQuery(CurrentUserId)));

    /// <summary>Adds a new payment method (card, mobile money account).</summary>
    [Authorize(Policy = "AppPolicy")]
    [HttpPost("payment-methods")]
    public async Task<IActionResult> AddPaymentMethod([FromBody] AddPaymentMethodRequest body)
        => Ok(await Mediator.Send(new AddPaymentMethodCommand(CurrentUserId, body)));

    /// <summary>Sets the specified payment method as the user's default.</summary>
    [Authorize(Policy = "AppPolicy")]
    [HttpPatch("payment-methods/{id}/default")]
    public async Task<IActionResult> SetDefaultPaymentMethod(string id)
    {
        await Mediator.Send(new SetDefaultPaymentMethodCommand(CurrentUserId, id));
        return NoContent();
    }

    /// <summary>Removes a saved payment method.</summary>
    [Authorize(Policy = "AppPolicy")]
    [HttpDelete("payment-methods/{id}")]
    public async Task<IActionResult> DeletePaymentMethod(string id)
    {
        await Mediator.Send(new DeletePaymentMethodCommand(CurrentUserId, id));
        return NoContent();
    }

    // ── Payment Intents ───────────────────────────────────────────────────────

    /// <summary>Creates a payment intent to initiate a charge through the gateway.</summary>
    [Authorize(Policy = "AppPolicy")]
    [HttpPost("payments/intents")]
    public async Task<IActionResult> CreatePaymentIntent(
        [FromBody] CreatePaymentIntentRequest body,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
        => Ok(await Mediator.Send(new CreatePaymentIntentCommand(
            CurrentUserId, body with { IdempotencyKey = idempotencyKey ?? body.IdempotencyKey })));

    /// <summary>Returns details of a specific payment.</summary>
    [Authorize(Policy = "AppPolicy")]
    [HttpGet("payments/{id}")]
    public async Task<IActionResult> GetPayment(string id)
        => Ok(await Mediator.Send(new GetPaymentQuery(CurrentUserId, id)));

    /// <summary>Cancels a pending payment intent.</summary>
    [Authorize(Policy = "AppPolicy")]
    [HttpPost("payments/{id}/cancel")]
    public async Task<IActionResult> CancelPayment(string id)
    {
        await Mediator.Send(new CancelPaymentCommand(CurrentUserId, id));
        return NoContent();
    }

    /// <summary>Returns a paginated list of the user's payment transactions.</summary>
    [Authorize(Policy = "AppPolicy")]
    [HttpGet("payments")]
    public async Task<IActionResult> GetPayments(
        [FromQuery] string? purpose,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int per_page = 20)
    {
        var (items, total) = await Mediator.Send(
            new GetPaymentsQuery(CurrentUserId, purpose, status, page, per_page));
        return Ok(items, PagedMeta.From(page, per_page, total));
    }

    // ── Webhooks ──────────────────────────────────────────────────────────────

    /// <summary>Receives and processes inbound payment-gateway webhook events.</summary>
    [AllowAnonymous]
    [HttpPost("webhooks/{gateway}")]
    public async Task<IActionResult> HandleWebhook(string gateway)
    {
        using var reader = new StreamReader(Request.Body);
        var rawPayload = await reader.ReadToEndAsync();
        var signature = Request.Headers["X-Signature"].FirstOrDefault()
                     ?? Request.Headers["x-cinetpay-signature"].FirstOrDefault();

        await Mediator.Send(new HandleWebhookCommand(gateway, rawPayload, signature));
        return Ok(new { received = true });
    }
}
