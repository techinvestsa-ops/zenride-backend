using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Payments.Dtos;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Payments.Commands;

// ── POST /payment-methods ─────────────────────────────────────────────────────

public record AddPaymentMethodCommand(string UserId, AddPaymentMethodRequest Request)
    : IRequest<PaymentMethodDto>;

public class AddPaymentMethodHandler(IApplicationDbContext db)
    : IRequestHandler<AddPaymentMethodCommand, PaymentMethodDto>
{
    public async Task<PaymentMethodDto> Handle(AddPaymentMethodCommand cmd, CancellationToken ct)
    {
        var req = cmd.Request;

        // Prevent duplicate mobile money numbers
        if (req.Phone != null)
        {
            var duplicate = await db.UserPaymentMethods
                .AnyAsync(m => m.UserId == cmd.UserId && m.Phone == req.Phone && m.Type == req.Type, ct);
            if (duplicate)
                throw new InvalidOperationException("CONFLICT: This payment method is already saved.");
        }

        var isFirst = !await db.UserPaymentMethods.AnyAsync(m => m.UserId == cmd.UserId, ct);

        var label = req.Type switch
        {
            "card"         => $"Card •••• {req.Last4 ?? "????"}",
            "orange_money" => $"Orange Money {req.Phone}",
            "moov_money"   => $"Moov Money {req.Phone}",
            "mtn_momo"     => $"MTN MoMo {req.Phone}",
            "wave"         => $"Wave {req.Phone}",
            _              => req.Type
        };

        var method = new UserPaymentMethod
        {
            UserId       = cmd.UserId,
            Type         = req.Type,
            Label        = label,
            Last4        = req.Last4,
            Phone        = req.Phone,
            GatewayToken = req.CardToken,
            IsDefault    = isFirst,
            IsVerified   = req.Phone != null,   // mobile money verified immediately; cards need gateway confirm
        };

        db.UserPaymentMethods.Add(method);
        await db.SaveChangesAsync(ct);

        return new PaymentMethodDto(method.Id, method.Type, method.Label, method.IsDefault, method.IsVerified);
    }
}

// ── PATCH /payment-methods/{id}/default ──────────────────────────────────────

public record SetDefaultPaymentMethodCommand(string UserId, string MethodId) : IRequest;

public class SetDefaultPaymentMethodHandler(IApplicationDbContext db)
    : IRequestHandler<SetDefaultPaymentMethodCommand>
{
    public async Task Handle(SetDefaultPaymentMethodCommand cmd, CancellationToken ct)
    {
        var methods = await db.UserPaymentMethods
            .Where(m => m.UserId == cmd.UserId)
            .ToListAsync(ct);

        var target = methods.FirstOrDefault(m => m.Id == cmd.MethodId)
            ?? throw new KeyNotFoundException("Payment method not found.");

        foreach (var m in methods) m.IsDefault = false;
        target.IsDefault = true;

        await db.SaveChangesAsync(ct);
    }
}

// ── DELETE /payment-methods/{id} ─────────────────────────────────────────────

public record DeletePaymentMethodCommand(string UserId, string MethodId) : IRequest;

public class DeletePaymentMethodHandler(IApplicationDbContext db)
    : IRequestHandler<DeletePaymentMethodCommand>
{
    public async Task Handle(DeletePaymentMethodCommand cmd, CancellationToken ct)
    {
        var method = await db.UserPaymentMethods
            .FirstOrDefaultAsync(m => m.Id == cmd.MethodId && m.UserId == cmd.UserId, ct)
            ?? throw new KeyNotFoundException("Payment method not found.");

        db.UserPaymentMethods.Remove(method);
        await db.SaveChangesAsync(ct);
    }
}

// ── POST /payments/intents ────────────────────────────────────────────────────

public record CreatePaymentIntentCommand(string UserId, CreatePaymentIntentRequest Request)
    : IRequest<PaymentIntentDto>;

public class CreatePaymentIntentHandler(IApplicationDbContext db, IPaymentGateway gateway)
    : IRequestHandler<CreatePaymentIntentCommand, PaymentIntentDto>
{
    public async Task<PaymentIntentDto> Handle(CreatePaymentIntentCommand cmd, CancellationToken ct)
    {
        var req = cmd.Request;

        if (!Enum.TryParse<PaymentMethod>(req.Method.Replace("_", ""), true, out var method))
            throw new ArgumentException("VALIDATION_ERROR: Invalid payment method.");

        // Idempotency: return existing intent for same key
        if (req.IdempotencyKey != null)
        {
            var existing = await db.Payments
                .FirstOrDefaultAsync(p => p.IdempotencyKey == req.IdempotencyKey &&
                                          p.UserId == cmd.UserId, ct);
            if (existing != null)
                return new PaymentIntentDto(existing.Id, existing.Status.ToString().ToLower(),
                    existing.CheckoutUrl, existing.UssdCode, existing.DeepLink, existing.OtpRequired);
        }

        var payment = new Payment
        {
            UserId          = cmd.UserId,
            Purpose         = req.Purpose,
            ReferenceId     = req.ReferenceId,
            Amount          = req.Amount,
            Currency        = req.Currency,
            Method          = method,
            Status          = Domain.Enums.PaymentStatus.Pending,
            IdempotencyKey  = req.IdempotencyKey,
            Market          = "ci",
        };

        db.Payments.Add(payment);
        await db.SaveChangesAsync(ct);

        var result = await gateway.InitiateAsync(
            payment.Id, req.Amount, req.Currency,
            req.Method, req.Phone, returnUrl: null, ct);

        payment.Status       = result.Status == "succeeded"
                               ? Domain.Enums.PaymentStatus.Succeeded
                               : Domain.Enums.PaymentStatus.Processing;
        payment.Gateway      = req.Method switch
        {
            "wave"              => "wave",
            "cash" or "wallet"  => "internal",
            _                   => "cinetpay"
        };
        payment.CheckoutUrl  = result.CheckoutUrl;
        payment.UssdCode     = result.UssdCode;
        payment.DeepLink     = result.DeepLink;
        payment.OtpRequired  = result.OtpRequired;

        await db.SaveChangesAsync(ct);

        return new PaymentIntentDto(payment.Id, result.Status,
            result.CheckoutUrl, result.UssdCode, result.DeepLink, result.OtpRequired);
    }
}

// ── POST /payments/{id}/cancel ────────────────────────────────────────────────

public record CancelPaymentCommand(string UserId, string PaymentId) : IRequest;

public class CancelPaymentHandler(IApplicationDbContext db)
    : IRequestHandler<CancelPaymentCommand>
{
    public async Task Handle(CancelPaymentCommand cmd, CancellationToken ct)
    {
        var payment = await db.Payments
            .FirstOrDefaultAsync(p => p.Id == cmd.PaymentId && p.UserId == cmd.UserId, ct)
            ?? throw new KeyNotFoundException("Payment not found.");

        if (payment.Status == Domain.Enums.PaymentStatus.Succeeded)
            throw new InvalidOperationException("CONFLICT: Cannot cancel a completed payment.");

        if (payment.Status == Domain.Enums.PaymentStatus.Cancelled)
            return;   // idempotent

        payment.Status = Domain.Enums.PaymentStatus.Cancelled;
        await db.SaveChangesAsync(ct);
    }
}

// ── POST /webhooks/{gateway} ──────────────────────────────────────────────────

public record HandleWebhookCommand(string Gateway, string RawPayload, string? Signature)
    : IRequest;

public class HandleWebhookHandler(IApplicationDbContext db, IPaymentGateway gateway)
    : IRequestHandler<HandleWebhookCommand>
{
    public async Task Handle(HandleWebhookCommand cmd, CancellationToken ct)
    {
        if (!gateway.VerifyWebhookSignature(cmd.Gateway, cmd.RawPayload, cmd.Signature))
            throw new UnauthorizedAccessException("FORBIDDEN: Invalid webhook signature.");

        using var doc = System.Text.Json.JsonDocument.Parse(cmd.RawPayload);
        var root = doc.RootElement;

        // Gateway-specific payload parsing
        var (gatewayRef, statusStr, succeeded, eventId) = cmd.Gateway.ToLower() switch
        {
            "cinetpay" => ParseCinetPay(root),
            "wave"     => ParseWave(root),
            _          => ParseGeneric(root)
        };

        // Idempotency: skip if this exact webhook event was already processed
        if (eventId != null)
        {
            var already = await db.WebhookEvents
                .AnyAsync(e => e.EventId == eventId && e.IsProcessed, ct);
            if (already) return;
        }

        var webhookEvent = new WebhookEvent
        {
            PaymentId   = gatewayRef ?? "unknown",
            Gateway     = cmd.Gateway,
            EventId     = eventId ?? Guid.CreateVersion7().ToString("N"),
            EventType   = statusStr ?? "unknown",
            RawPayload  = cmd.RawPayload,
            IsProcessed = false,
        };
        db.WebhookEvents.Add(webhookEvent);

        if (gatewayRef != null)
        {
            var payment = await db.Payments
                .FirstOrDefaultAsync(p => p.Id == gatewayRef ||
                                          p.GatewayReference == gatewayRef, ct);

            if (payment != null && payment.Status != Domain.Enums.PaymentStatus.Succeeded)
            {
                payment.Status           = succeeded
                    ? Domain.Enums.PaymentStatus.Succeeded
                    : Domain.Enums.PaymentStatus.Failed;
                payment.GatewayReference = gatewayRef;
                webhookEvent.PaymentId   = payment.Id;

                if (succeeded && payment.Purpose == "topup")
                {
                    var wallet = await db.Wallets
                        .FirstOrDefaultAsync(w => w.UserId == payment.UserId, ct);

                    if (wallet != null)
                    {
                        wallet.Balance += payment.Amount;
                        db.WalletTransactions.Add(new WalletTransaction
                        {
                            WalletId     = wallet.Id,
                            Type         = Domain.Enums.WalletTransactionType.Topup,
                            Amount       = payment.Amount,
                            BalanceAfter = wallet.Balance,
                            Title        = "Top-up",
                            Subtitle     = $"via {cmd.Gateway}",
                            ReferenceId  = payment.Id,
                            Status       = Domain.Enums.PaymentStatus.Succeeded,
                        });
                    }
                }
            }
        }

        webhookEvent.IsProcessed = true;
        webhookEvent.ProcessedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    // ── Gateway-specific parsers ──────────────────────────────────────────────

    // CinetPay webhook fields: cpm_trans_id, cpm_result ("00" = ok), cpm_trans_status ("ACCEPTED")
    private static (string? Ref, string? Status, bool Succeeded, string? EventId)
        ParseCinetPay(System.Text.Json.JsonElement root)
    {
        var transId   = root.TryGetProperty("cpm_trans_id", out var t) ? t.GetString() : null;
        var result    = root.TryGetProperty("cpm_result", out var r) ? r.GetString() : null;
        var status    = root.TryGetProperty("cpm_trans_status", out var s) ? s.GetString() : null;
        var succeeded = result == "00" && status == "ACCEPTED";
        return (transId, status, succeeded, transId);   // transId is the event idempotency key
    }

    // Wave webhook fields: data.client_reference, data.checkout_status ("complete")
    private static (string? Ref, string? Status, bool Succeeded, string? EventId)
        ParseWave(System.Text.Json.JsonElement root)
    {
        var data      = root.TryGetProperty("data", out var d) ? d : default;
        var clientRef = data.ValueKind != System.Text.Json.JsonValueKind.Undefined &&
                        data.TryGetProperty("client_reference", out var cr)
                        ? cr.GetString() : null;
        var status    = data.ValueKind != System.Text.Json.JsonValueKind.Undefined &&
                        data.TryGetProperty("checkout_status", out var cs)
                        ? cs.GetString() : null;
        var sessionId = data.ValueKind != System.Text.Json.JsonValueKind.Undefined &&
                        data.TryGetProperty("id", out var id)
                        ? id.GetString() : null;
        var succeeded = status == "complete";
        return (clientRef, status, succeeded, sessionId);
    }

    // Fallback for unknown gateways
    private static (string? Ref, string? Status, bool Succeeded, string? EventId)
        ParseGeneric(System.Text.Json.JsonElement root)
    {
        var gatewayRef = root.TryGetProperty("transaction_id", out var tid) ? tid.GetString()
                       : root.TryGetProperty("reference", out var rref) ? rref.GetString()
                       : null;
        var statusStr  = root.TryGetProperty("status", out var s) ? s.GetString() : null;
        var succeeded  = statusStr is "ACCEPTED" or "SUCCESS" or "succeeded";
        return (gatewayRef, statusStr, succeeded, gatewayRef);
    }
}
