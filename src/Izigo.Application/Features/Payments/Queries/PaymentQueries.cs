using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Payments.Dtos;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Payments.Queries;

// ── GET /payment-methods ──────────────────────────────────────────────────────

public record GetPaymentMethodsQuery(string UserId) : IRequest<List<PaymentMethodDto>>;

public class GetPaymentMethodsHandler(IApplicationDbContext db)
    : IRequestHandler<GetPaymentMethodsQuery, List<PaymentMethodDto>>
{
    public async Task<List<PaymentMethodDto>> Handle(GetPaymentMethodsQuery req, CancellationToken ct)
    {
        var saved = await db.UserPaymentMethods
            .Where(m => m.UserId == req.UserId)
            .OrderByDescending(m => m.IsDefault)
            .Select(m => new PaymentMethodDto(m.Id, m.Type, m.Label, m.IsDefault, m.IsVerified))
            .ToListAsync(ct);

        // Always include cash and wallet as system methods
        var system = new List<PaymentMethodDto>
        {
            new("_cash",   "cash",   "Cash",   IsDefault: false, IsVerified: true),
            new("_wallet", "wallet", "Wallet", IsDefault: false, IsVerified: true),
        };

        return [.. saved, .. system];
    }
}

// ── GET /payments/{id} ────────────────────────────────────────────────────────

public record GetPaymentQuery(string UserId, string PaymentId) : IRequest<PaymentDto>;

public class GetPaymentHandler(IApplicationDbContext db)
    : IRequestHandler<GetPaymentQuery, PaymentDto>
{
    public async Task<PaymentDto> Handle(GetPaymentQuery req, CancellationToken ct)
    {
        var p = await db.Payments
            .FirstOrDefaultAsync(p => p.Id == req.PaymentId && p.UserId == req.UserId, ct)
            ?? throw new KeyNotFoundException("Payment not found.");

        return PaymentMapper.Map(p);
    }
}

// ── GET /payments ─────────────────────────────────────────────────────────────

public record GetPaymentsQuery(string UserId, string? Purpose, string? Status, int Page, int PerPage)
    : IRequest<(List<PaymentDto> Items, int Total)>;

public class GetPaymentsHandler(IApplicationDbContext db)
    : IRequestHandler<GetPaymentsQuery, (List<PaymentDto>, int)>
{
    public async Task<(List<PaymentDto>, int)> Handle(GetPaymentsQuery req, CancellationToken ct)
    {
        var q = db.Payments.Where(p => p.UserId == req.UserId);

        if (!string.IsNullOrEmpty(req.Purpose))
            q = q.Where(p => p.Purpose == req.Purpose);

        if (!string.IsNullOrEmpty(req.Status) &&
            Enum.TryParse<PaymentStatus>(req.Status, true, out var st))
            q = q.Where(p => p.Status == st);

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(p => p.CreatedAt)
            .Skip((req.Page - 1) * req.PerPage)
            .Take(req.PerPage)
            .Select(p => PaymentMapper.Map(p))
            .ToListAsync(ct);

        return (items, total);
    }
}

internal static class PaymentMapper
{
    public static PaymentDto Map(Domain.Entities.Payment p) =>
        new(p.Id, p.Purpose, p.ReferenceId, p.Amount, p.Currency,
            p.Method.ToString().ToLower(), p.Status.ToString().ToLower(), p.CreatedAt);
}
