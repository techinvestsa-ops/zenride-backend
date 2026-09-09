using Izigo.Application.Features.Admin.Finance.Commands;
using Izigo.Application.Features.Admin.Finance.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Izigo.Api.Controllers.V1.Admin;

/// <summary>
/// Admin finance controller: payments (A10), wallets &amp; ledger (A11),
/// payouts &amp; cash settlement (A12), pricing / commission / surge (A13),
/// and reconciliation (A14).
/// </summary>
[Authorize(Policy = "AdminPolicy")]
public class FinanceAdminController : AdminBaseController
{
    // ── A10 Payments ──────────────────────────────────────────────────────────

    /// <summary>Returns every payment attempt. meta.facets.status powers the filter chip counts.</summary>
    [HttpGet("api/v1/admin/payments")]
    public async Task<IActionResult> GetPayments(
        [FromQuery] string? status, [FromQuery] string? gateway,
        [FromQuery] string? method, [FromQuery] string? purpose, [FromQuery] string? fail_reason,
        [FromQuery] long? min_amount, [FromQuery] long? max_amount,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? q,
        [FromQuery] int page = 1, [FromQuery] int per_page = 25, CancellationToken ct = default)
    {
        var check = CheckPermission("payments.view");
        if (check is not null) return check;
        return Ok(await Mediator.Send(new GetAdminPaymentsQuery(Market, status, gateway, method,
            purpose, fail_reason, min_amount, max_amount, from, to, q, page, per_page), ct));
    }

    /// <summary>Returns gateway reference, idempotency key, full webhook_events[] with raw payloads, ledger entries.</summary>
    [HttpGet("api/v1/admin/payments/{id}")]
    public async Task<IActionResult> GetPayment(string id, CancellationToken ct = default)
    {
        var check = CheckPermission("payments.view");
        if (check is not null) return check;

        var dto = await Mediator.Send(new GetAdminPaymentDetailQuery(id), ct);
        if (dto is null) return NotFound(new { success = false, error = new { code = "PAYMENT_NOT_FOUND" } });
        return Ok(new { success = true, data = dto });
    }

    /// <summary>Returns volume and failure rate by gateway and by reason code.</summary>
    [HttpGet("api/v1/admin/payments/stats")]
    public async Task<IActionResult> GetPaymentStats(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct = default)
    {
        var check = CheckPermission("payments.view");
        if (check is not null) return check;
        return Ok(new { success = true, data = await Mediator.Send(
            new GetAdminPaymentStatsQuery(Market, from, to), ct) });
    }

    /// <summary>Retries a failed payment with a new idempotency key linked to the original.</summary>
    [HttpPost("api/v1/admin/payments/{id}/retry")]
    public async Task<IActionResult> RetryPayment(string id, CancellationToken ct = default)
    {
        var check = CheckPermission("payments.write");
        if (check is not null) return check;

        var result = await Mediator.Send(
            new RetryPaymentCommand(id, StaffId, CurrentStaff.Email ?? StaffId), ct);

        return result.Success
            ? Ok(new { success = true, data = result.Data })
            : result.ErrorCode == "PAYMENT_NOT_FOUND"
                ? NotFound(new { success = false, error = new { code = result.ErrorCode } })
                : UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } });
    }

    /// <summary>Issues a payment-level refund (for topups and duplicates, not trip refunds).</summary>
    [HttpPost("api/v1/admin/payments/{id}/refund")]
    public async Task<IActionResult> RefundPayment(string id,
        [FromBody] PaymentRefundRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("payments.write");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        var result = await Mediator.Send(
            new RefundPaymentCommand(id, req.Amount, req.Reason, StaffId, CurrentStaff.Email ?? StaffId), ct);

        return result.Success
            ? Ok(new { success = true })
            : result.ErrorCode == "PAYMENT_NOT_FOUND"
                ? NotFound(new { success = false, error = new { code = result.ErrorCode } })
                : UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } });
    }

    /// <summary>Re-processes a gateway webhook. Idempotent by gateway event id.</summary>
    [HttpPost("api/v1/admin/payments/{id}/replay-webhook")]
    public async Task<IActionResult> ReplayWebhook(string id,
        [FromBody] ReplayWebhookRequest? req, CancellationToken ct = default)
    {
        var check = CheckPermission("payments.write");
        if (check is not null) return check;

        var result = await Mediator.Send(
            new ReplayWebhookCommand(id, req?.EventId, StaffId, CurrentStaff.Email ?? StaffId), ct);

        return result.Success
            ? Ok(new { success = true })
            : NotFound(new { success = false, error = new { code = result.ErrorCode } });
    }

    /// <summary>Marks a payment as manually reconciled (settled out of band).</summary>
    [HttpPost("api/v1/admin/payments/{id}/mark-reconciled")]
    public async Task<IActionResult> MarkReconciled(string id,
        [FromBody] MarkReconciledRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("payments.write");
        if (check is not null) return check;

        var result = await Mediator.Send(
            new MarkReconciledCommand(id, req.Note, StaffId, CurrentStaff.Email ?? StaffId), ct);

        return result.Success
            ? Ok(new { success = true })
            : NotFound(new { success = false, error = new { code = result.ErrorCode } });
    }

    /// <summary>Exports payment records. Finance takes this monthly.</summary>
    [HttpGet("api/v1/admin/payments/export")]
    public async Task<IActionResult> ExportPayments(CancellationToken ct = default)
    {
        var check = CheckPermission("payments.view");
        if (check is not null) return check;

        var (jobId, statusUrl) = await Mediator.Send(new ExportPaymentsQuery(StaffId), ct);
        return Accepted(new { success = true, data = new { job_id = jobId, status_url = statusUrl } });
    }

    // ── A11 Wallets ───────────────────────────────────────────────────────────

    /// <summary>Returns all wallets (rider and/or driver). Filters: kind, min_balance, frozen.</summary>
    [HttpGet("api/v1/admin/wallets")]
    public async Task<IActionResult> GetWallets(
        [FromQuery] string? kind, [FromQuery] long? min_balance, [FromQuery] bool? frozen,
        [FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int per_page = 25,
        CancellationToken ct = default)
    {
        var check = CheckPermission("wallets.view");
        if (check is not null) return check;
        return Ok(await Mediator.Send(new GetAdminWalletsQuery(kind, min_balance, frozen, q, page, per_page), ct));
    }

    /// <summary>Returns rider_float, driver_float, cash_held_by_drivers, net_platform_position. Computed from ledger.</summary>
    [HttpGet("api/v1/admin/wallets/totals")]
    public async Task<IActionResult> GetWalletTotals(CancellationToken ct = default)
    {
        var check = CheckPermission("wallets.view");
        if (check is not null) return check;
        return Ok(new { success = true, data = await Mediator.Send(new GetWalletTotalsQuery(), ct) });
    }

    /// <summary>Returns the full ledger for a wallet: type, signed amount, balance_after, reference_id, created_by, reason.</summary>
    [HttpGet("api/v1/admin/wallets/{id}/ledger")]
    public async Task<IActionResult> GetWalletLedger(string id,
        [FromQuery] int page = 1, [FromQuery] int per_page = 50, CancellationToken ct = default)
    {
        var check = CheckPermission("wallets.view");
        if (check is not null) return check;

        var result = await Mediator.Send(new GetWalletLedgerQuery(id, page, per_page), ct);
        if (result is null) return NotFound(new { success = false, error = new { code = "WALLET_NOT_FOUND" } });
        return Ok(result);
    }

    /// <summary>Applies a manual credit or debit adjustment. Requires wallets.adjust + Idempotency-Key.</summary>
    [HttpPost("api/v1/admin/wallets/{id}/adjust")]
    public async Task<IActionResult> AdjustWallet(string id,
        [FromBody] WalletAdjustRequest req,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct = default)
    {
        var check = CheckPermission("wallets.adjust");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return BadRequest(new { success = false, error = new { code = "IDEMPOTENCY_KEY_REQUIRED" } });

        if (req.Direction is not ("credit" or "debit"))
            return BadRequest(new { success = false, error = new { code = "INVALID_DIRECTION" } });

        var result = await Mediator.Send(new AdjustWalletCommand(
            id, req.Direction, req.Amount, req.Reason, idempotencyKey,
            StaffId, CurrentStaff.Email ?? StaffId), ct);

        return result.Success
            ? Ok(new { success = true })
            : result.ErrorCode == "WALLET_NOT_FOUND"
                ? NotFound(new { success = false, error = new { code = result.ErrorCode } })
                : BadRequest(new { success = false, error = new { code = result.ErrorCode } });
    }

    /// <summary>Freezes or unfreezes a wallet. Blocks spend and withdrawal, not earning.</summary>
    [HttpPost("api/v1/admin/wallets/{id}/freeze")]
    public async Task<IActionResult> FreezeWallet(string id,
        [FromBody] ReasonOnlyRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("wallets.freeze");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        var result = await Mediator.Send(
            new FreezeWalletCommand(id, req.Reason, StaffId, CurrentStaff.Email ?? StaffId), ct);

        return result.Success
            ? Ok(new { success = true })
            : NotFound(new { success = false, error = new { code = result.ErrorCode } });
    }

    /// <summary>Returns every manual wallet adjustment with actor and reason, filterable by actor.</summary>
    [HttpGet("api/v1/admin/wallets/adjustments")]
    public async Task<IActionResult> GetWalletAdjustments(
        [FromQuery] string? actor, [FromQuery] int page = 1,
        [FromQuery] int per_page = 25, CancellationToken ct = default)
    {
        var check = CheckPermission("wallets.view");
        if (check is not null) return check;
        return Ok(await Mediator.Send(new GetWalletAdjustmentsQuery(actor, page, per_page), ct));
    }

    // ── A12 Payouts ───────────────────────────────────────────────────────────

    /// <summary>Returns the payout queue. Fields include driver_cash_owed so reviewer sees the offset without opening the row.</summary>
    [HttpGet("api/v1/admin/payouts")]
    public async Task<IActionResult> GetPayouts(
        [FromQuery] string? status, [FromQuery] string? bank,
        [FromQuery] long? min_amount, [FromQuery] long? max_amount,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int page = 1, [FromQuery] int per_page = 25, CancellationToken ct = default)
    {
        var check = CheckPermission("payouts.view");
        if (check is not null) return check;
        return Ok(await Mediator.Send(new GetAdminPayoutsQuery(
            Market, status, bank, min_amount, max_amount, from, to, page, per_page), ct));
    }

    /// <summary>Returns payout detail with attempt history, provider reference, and name-enquiry result.</summary>
    [HttpGet("api/v1/admin/payouts/{id}")]
    public async Task<IActionResult> GetPayout(string id, CancellationToken ct = default)
    {
        var check = CheckPermission("payouts.view");
        if (check is not null) return check;

        var dto = await Mediator.Send(new GetAdminPayoutDetailQuery(id), ct);
        if (dto is null) return NotFound(new { success = false, error = new { code = "PAYOUT_NOT_FOUND" } });
        return Ok(new { success = true, data = dto });
    }

    /// <summary>Approves and sends a payout. Must be idempotent — a double-approve is a double payment.</summary>
    [HttpPost("api/v1/admin/payouts/{id}/approve")]
    public async Task<IActionResult> ApprovePayout(string id, CancellationToken ct = default)
    {
        var check = CheckPermission("payouts.approve");
        if (check is not null) return check;

        var result = await Mediator.Send(
            new ApprovePayoutCommand(id, StaffId, CurrentStaff.Email ?? StaffId, Market), ct);

        return result.Success
            ? Ok(new { success = true })
            : result.ErrorCode == "PAYOUT_NOT_FOUND"
                ? NotFound(new { success = false, error = new { code = result.ErrorCode } })
                : UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } });
    }

    /// <summary>Rejects a payout and returns the funds to the driver's available balance.</summary>
    [HttpPost("api/v1/admin/payouts/{id}/reject")]
    public async Task<IActionResult> RejectPayout(string id,
        [FromBody] ReasonOnlyRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("payouts.approve");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        var result = await Mediator.Send(
            new RejectPayoutCommand(id, req.Reason, StaffId, CurrentStaff.Email ?? StaffId, Market), ct);

        return result.Success
            ? Ok(new { success = true })
            : result.ErrorCode == "PAYOUT_NOT_FOUND"
                ? NotFound(new { success = false, error = new { code = result.ErrorCode } })
                : UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } });
    }

    /// <summary>Retries a failed payout disbursement after correcting bank details.</summary>
    [HttpPost("api/v1/admin/payouts/{id}/retry")]
    public async Task<IActionResult> RetryPayout(string id, CancellationToken ct = default)
    {
        var check = CheckPermission("payouts.approve");
        if (check is not null) return check;

        var result = await Mediator.Send(
            new RetryPayoutCommand(id, StaffId, CurrentStaff.Email ?? StaffId, Market), ct);

        return result.Success
            ? Ok(new { success = true })
            : result.ErrorCode == "PAYOUT_NOT_FOUND"
                ? NotFound(new { success = false, error = new { code = result.ErrorCode } })
                : UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } });
    }

    /// <summary>Settles the driver's cash liability against this payout in one transaction.</summary>
    [HttpPost("api/v1/admin/payouts/{id}/net-off-cash")]
    public async Task<IActionResult> NetOffCash(string id, CancellationToken ct = default)
    {
        var check = CheckPermission("payouts.approve");
        if (check is not null) return check;

        var result = await Mediator.Send(
            new NetOffCashCommand(id, StaffId, CurrentStaff.Email ?? StaffId, Market), ct);

        if (!result.Success)
            return result.ErrorCode == "PAYOUT_NOT_FOUND"
                ? NotFound(new { success = false, error = new { code = result.ErrorCode } })
                : UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true, data = result.Data });
    }

    /// <summary>Bulk-approves payouts. Returns 202 + job_id. Partial failure reports per-id outcomes.</summary>
    [HttpPost("api/v1/admin/payouts/bulk-approve")]
    public async Task<IActionResult> BulkApprovePayout(
        [FromBody] BulkApproveRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("payouts.approve");
        if (check is not null) return check;

        if (!req.Ids.Any())
            return BadRequest(new { success = false, error = new { code = "IDS_REQUIRED" } });

        var (jobId, statusUrl) = await Mediator.Send(new BulkApprovePayoutsCommand(
            req.Ids, StaffId, CurrentStaff.Email ?? StaffId, Market), ct);
        return Accepted(new { success = true, data = new { job_id = jobId, status_url = statusUrl } });
    }

    // ── A12 Cash Settlement ───────────────────────────────────────────────────

    /// <summary>Returns outstanding cash balances ranked by amount, with days outstanding and cap status.</summary>
    [HttpGet("api/v1/admin/cash-settlement")]
    public async Task<IActionResult> GetCashSettlement(CancellationToken ct = default)
    {
        var check = CheckPermission("payouts.view");
        if (check is not null) return check;
        return Ok(await Mediator.Send(new GetCashSettlementQuery(Market), ct));
    }

    /// <summary>Records a cash payment received from a driver at an office.</summary>
    [HttpPost("api/v1/admin/cash-settlement/{driverId}/record")]
    public async Task<IActionResult> RecordCashSettlement(string driverId,
        [FromBody] RecordCashRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("payouts.approve");
        if (check is not null) return check;

        var result = await Mediator.Send(new RecordCashSettlementCommand(
            driverId, req.Amount, req.Method, req.ReceiptRef,
            StaffId, CurrentStaff.Email ?? StaffId, Market), ct);

        return result.Success
            ? Ok(new { success = true })
            : result.ErrorCode == "DRIVER_NOT_FOUND"
                ? NotFound(new { success = false, error = new { code = result.ErrorCode } })
                : UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } });
    }

    /// <summary>Writes off an irrecoverable cash balance. High privilege.</summary>
    [HttpPost("api/v1/admin/cash-settlement/{driverId}/write-off")]
    public async Task<IActionResult> WriteOffCashSettlement(string driverId,
        [FromBody] WriteOffRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("payouts.approve");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        var result = await Mediator.Send(new WriteOffCashSettlementCommand(
            driverId, req.Amount, req.Reason, StaffId, CurrentStaff.Email ?? StaffId, Market), ct);

        return result.Success
            ? Ok(new { success = true })
            : result.ErrorCode == "DRIVER_NOT_FOUND"
                ? NotFound(new { success = false, error = new { code = result.ErrorCode } })
                : UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } });
    }

    // ── A13 Pricing ───────────────────────────────────────────────────────────

    /// <summary>Returns all active fare rules per service class.</summary>
    [HttpGet("api/v1/admin/pricing/fare-rules")]
    public async Task<IActionResult> GetFareRules(CancellationToken ct = default)
    {
        var check = CheckPermission("pricing.view");
        if (check is not null) return check;
        return Ok(await Mediator.Send(new GetFareRulesQuery(Market), ct));
    }

    /// <summary>Updates a fare rule, creating a new version. In-flight trips keep their quoted version. Permission: pricing.write.</summary>
    [HttpPut("api/v1/admin/pricing/fare-rules/{serviceClass}")]
    public async Task<IActionResult> UpsertFareRule(string serviceClass,
        [FromBody] FareRuleRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("pricing.write");
        if (check is not null) return check;

        var result = await Mediator.Send(new UpdateFareRuleCommand(Market, serviceClass,
            req.Base, req.PerKm, req.PerMin, req.Minimum, req.WaitingPerMin,
            req.CancellationFee, req.EffectiveFrom,
            StaffId, CurrentStaff.Email ?? StaffId), ct);

        return result.Success
            ? Ok(new { success = true, data = result.Data })
            : BadRequest(new { success = false, error = new { code = result.ErrorCode } });
    }

    /// <summary>Returns the full version history of fare rule changes.</summary>
    [HttpGet("api/v1/admin/pricing/fare-rules/history")]
    public async Task<IActionResult> GetFareRuleHistory(
        [FromQuery] string? service_class, CancellationToken ct = default)
    {
        var check = CheckPermission("pricing.view");
        if (check is not null) return check;
        return Ok(await Mediator.Send(new GetFareRuleHistoryQuery(Market, service_class), ct));
    }

    /// <summary>Returns commission and bonus configuration. Resolves the 20%/25% disagreement in the driver app.</summary>
    [HttpGet("api/v1/admin/pricing/commission")]
    public async Task<IActionResult> GetCommission(CancellationToken ct = default)
    {
        var check = CheckPermission("pricing.view");
        if (check is not null) return check;
        return Ok(new { success = true, data = await Mediator.Send(new GetCommissionQuery(Market), ct) });
    }

    /// <summary>Updates platform commission. Versioned and scheduled the same way as fare rules.</summary>
    [HttpPut("api/v1/admin/pricing/commission")]
    public async Task<IActionResult> UpdateCommission(
        [FromBody] CommissionRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("pricing.write");
        if (check is not null) return check;

        var result = await Mediator.Send(new UpdateCommissionCommand(Market,
            req.CommissionRate, req.BonusRate, req.BonusLabel, req.CashSettlementCap,
            req.VerticalOverridesJson, req.EffectiveFrom,
            StaffId, CurrentStaff.Email ?? StaffId), ct);

        return result.Success
            ? Ok(new { success = true, data = result.Data })
            : BadRequest(new { success = false, error = new { code = result.ErrorCode } });
    }

    /// <summary>Returns live surge multipliers per zone, with whether each is automatic or manual.</summary>
    [HttpGet("api/v1/admin/pricing/surge")]
    public async Task<IActionResult> GetSurge(CancellationToken ct = default)
    {
        var check = CheckPermission("pricing.view");
        if (check is not null) return check;
        return Ok(await Mediator.Send(new GetSurgeQuery(Market), ct));
    }

    /// <summary>Overrides surge for a zone. Above 1.6× returns 422 SECOND_APPROVER_REQUIRED.</summary>
    [HttpPut("api/v1/admin/pricing/surge/{zoneId}")]
    public async Task<IActionResult> UpdateSurge(string zoneId,
        [FromBody] SurgeRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("pricing.write");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        var result = await Mediator.Send(new UpdateSurgeCommand(
            zoneId, req.Multiplier, req.ExpiresAt, req.Reason,
            StaffId, CurrentStaff.Email ?? StaffId, Market), ct);

        if (!result.Success)
        {
            if (result.ErrorCode == "SECOND_APPROVER_REQUIRED")
                return UnprocessableEntity(new
                {
                    success = false,
                    error = new { code = result.ErrorCode,
                        message = "Surge above 1.6× requires a second approver." }
                });
            return NotFound(new { success = false, error = new { code = result.ErrorCode } });
        }
        return Ok(new { success = true });
    }

    /// <summary>Replays last week's trips against proposed fare rules and returns the revenue delta.</summary>
    [HttpPost("api/v1/admin/pricing/simulate")]
    public async Task<IActionResult> SimulateFare(
        [FromBody] SimulateRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("pricing.view");
        if (check is not null) return check;

        return Ok(new { success = true, data = await Mediator.Send(new SimulateFareQuery(
            Market, req.ServiceClass, req.Base, req.PerKm, req.PerMin, req.Minimum), ct) });
    }

    // ── A14 Reconciliation ────────────────────────────────────────────────────

    /// <summary>Returns settled vs ledger per gateway per day: transactions, variance, missed_webhooks, status.</summary>
    [HttpGet("api/v1/admin/reconciliation")]
    public async Task<IActionResult> GetReconciliation(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct = default)
    {
        var check = CheckPermission("recon.view");
        if (check is not null) return check;
        return Ok(await Mediator.Send(new GetReconciliationQuery(Market, from, to), ct));
    }

    /// <summary>Returns the specific transactions that do not match for a gateway.</summary>
    [HttpGet("api/v1/admin/reconciliation/{gateway}/variances")]
    public async Task<IActionResult> GetVariances(string gateway,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct = default)
    {
        var check = CheckPermission("recon.view");
        if (check is not null) return check;
        return Ok(await Mediator.Send(
            new GetReconciliationVariancesQuery(gateway, Market, from, to), ct));
    }

    /// <summary>Uploads a gateway CSV statement and matches it against the ledger. Returns 202 + job_id.</summary>
    [HttpPost("api/v1/admin/reconciliation/import-statement")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ImportStatement(
        IFormFile? file, CancellationToken ct = default)
    {
        var check = CheckPermission("recon.view");
        if (check is not null) return check;

        byte[]? content = null;
        if (file is not null)
        {
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            content = ms.ToArray();
        }

        var (jobId, statusUrl) = await Mediator.Send(
            new ImportStatementCommand(StaffId, Market, content), ct);
        return Accepted(new { success = true, data = new { job_id = jobId, status_url = statusUrl } });
    }

    /// <summary>Batch-replays all missed webhook events to reconcile out-of-sync transactions.</summary>
    [HttpPost("api/v1/admin/reconciliation/replay-missed")]
    public async Task<IActionResult> ReplayMissed(
        [FromBody] ReplayMissedRequest? req, CancellationToken ct = default)
    {
        var check = CheckPermission("recon.view");
        if (check is not null) return check;

        var (jobId, statusUrl) = await Mediator.Send(new ReplayMissedCommand(
            Market, req?.Gateway, StaffId, CurrentStaff.Email ?? StaffId), ct);
        return Accepted(new { success = true, data = new { job_id = jobId, status_url = statusUrl } });
    }
}

// ── Request shapes ────────────────────────────────────────────────────────────

public record PaymentRefundRequest(long? Amount, string Reason);
public record ReplayWebhookRequest(string? EventId);
public record MarkReconciledRequest(string Note);
public record WalletAdjustRequest(string Direction, long Amount, string Reason);
public record BulkApproveRequest(List<string> Ids);
public record RecordCashRequest(long Amount, string Method, string ReceiptRef);
public record WriteOffRequest(long Amount, string Reason);
public record FareRuleRequest(long Base, long PerKm, long PerMin, long Minimum,
    long WaitingPerMin, long CancellationFee, DateTime? EffectiveFrom);
public record CommissionRequest(decimal CommissionRate, decimal BonusRate, string? BonusLabel,
    long? CashSettlementCap, string? VerticalOverridesJson, DateTime? EffectiveFrom);
public record SurgeRequest(decimal Multiplier, DateTime? ExpiresAt, string Reason);
public record SimulateRequest(string ServiceClass, long Base, long PerKm, long PerMin, long Minimum);
public record ReplayMissedRequest(string? Gateway);
