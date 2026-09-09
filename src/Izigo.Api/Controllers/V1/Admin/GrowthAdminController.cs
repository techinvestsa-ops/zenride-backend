using Izigo.Application.Features.Admin.Growth.Commands;
using Izigo.Application.Features.Admin.Growth.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Izigo.Api.Controllers.V1.Admin;

[Route("api/v1/admin")]
[Authorize(Policy = "AdminPolicy")]
public class GrowthAdminController : AdminBaseController
{
    // ── A15: Coupons ──────────────────────────────────────────────────────────

    [HttpGet("coupons")]
    public async Task<IActionResult> GetCoupons(
        [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int per_page = 25,
        CancellationToken ct = default)
    {
        var check = CheckPermission("dashboard.view");
        if (check is not null) return check;

        return Ok(await Mediator.Send(
            new GetAdminCouponsQuery(Market, status, page, per_page), ct));
    }

    [HttpPost("coupons")]
    public async Task<IActionResult> CreateCoupon(
        [FromBody] CreateCouponRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("pricing.write");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Code))
            return BadRequest(new { success = false, error = new { code = "CODE_REQUIRED" } });

        var result = await Mediator.Send(new CreateCouponCommand(
            req.Code, req.Title, req.DiscountType, req.Value,
            req.MaxDiscount, req.MinOrder, req.Verticals ?? [],
            req.Zones ?? [], req.FirstTripOnly, req.PerUserLimit,
            req.TotalCap, req.StartsAt, req.ExpiresAt,
            Market, StaffId, CurrentStaff.Email ?? StaffId), ct);

        if (!result.Success)
            return Conflict(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true, data = result.Data });
    }

    [HttpPatch("coupons/{code}")]
    public async Task<IActionResult> UpdateCoupon(string code,
        [FromBody] UpdateCouponRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("pricing.write");
        if (check is not null) return check;

        var result = await Mediator.Send(new UpdateCouponCommand(
            code, Market, req.Title, req.DiscountType, req.Value,
            req.MaxDiscount, req.MinOrder, req.Verticals, req.Zones,
            req.FirstTripOnly, req.PerUserLimit, req.TotalCap,
            req.StartsAt, req.ExpiresAt, req.IsActive,
            StaffId, CurrentStaff.Email ?? StaffId), ct);

        if (!result.Success)
            return result.ErrorCode == "COUPON_NOT_FOUND"
                ? NotFound(new { success = false, error = new { code = result.ErrorCode } })
                : UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true });
    }

    [HttpGet("coupons/{code}/performance")]
    public async Task<IActionResult> GetCouponPerformance(string code, CancellationToken ct = default)
    {
        var check = CheckPermission("dashboard.view");
        if (check is not null) return check;

        var dto = await Mediator.Send(new GetCouponPerformanceQuery(code, Market), ct);
        if (dto is null) return NotFound(new { success = false, error = new { code = "COUPON_NOT_FOUND" } });
        return Ok(new { success = true, data = dto });
    }

    // ── A15: Referrals ────────────────────────────────────────────────────────

    [HttpGet("referrals")]
    public async Task<IActionResult> GetReferrals(
        [FromQuery] int page = 1, [FromQuery] int per_page = 25,
        CancellationToken ct = default)
    {
        var check = CheckPermission("dashboard.view");
        if (check is not null) return check;

        return Ok(await Mediator.Send(new GetAdminReferralsQuery(Market, page, per_page), ct));
    }

    [HttpPut("referrals/config")]
    public async Task<IActionResult> UpdateReferralConfig(
        [FromBody] UpdateReferralConfigRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("pricing.write");
        if (check is not null) return check;

        var result = await Mediator.Send(new UpdateReferralConfigCommand(
            req.RewardReferrer, req.RewardReferred,
            req.QualificationTrips, req.Cap,
            Market, StaffId, CurrentStaff.Email ?? StaffId), ct);

        if (!result.Success)
            return UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true, data = result.Data });
    }

    // ── A15: Incentives ───────────────────────────────────────────────────────

    [HttpGet("incentives")]
    public async Task<IActionResult> GetIncentives(
        [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int per_page = 25,
        CancellationToken ct = default)
    {
        var check = CheckPermission("dashboard.view");
        if (check is not null) return check;

        return Ok(await Mediator.Send(
            new GetAdminIncentivesQuery(Market, status, page, per_page), ct));
    }

    [HttpPost("incentives")]
    public async Task<IActionResult> CreateIncentive(
        [FromBody] CreateIncentiveRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("pricing.write");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest(new { success = false, error = new { code = "TITLE_REQUIRED" } });

        var result = await Mediator.Send(new CreateIncentiveCommand(
            req.Title, req.Audience, req.Condition ?? new { },
            req.Reward, req.Currency ?? "XOF", req.BudgetCap,
            req.StartsAt, req.EndsAt,
            Market, StaffId, CurrentStaff.Email ?? StaffId), ct);

        if (!result.Success)
            return UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true, data = result.Data });
    }

    // ── A16: Support Tickets ──────────────────────────────────────────────────

    [HttpGet("support/tickets")]
    public async Task<IActionResult> GetTickets(
        [FromQuery] string? status, [FromQuery] string? category,
        [FromQuery] string? priority, [FromQuery] string? assignee,
        [FromQuery] string? audience, [FromQuery] bool? past_sla,
        [FromQuery] int page = 1, [FromQuery] int per_page = 25,
        CancellationToken ct = default)
    {
        var check = CheckPermission("dashboard.view");
        if (check is not null) return check;

        return Ok(await Mediator.Send(new GetAdminTicketsQuery(
            Market, status, category, priority, assignee, audience, past_sla, page, per_page), ct));
    }

    [HttpGet("support/tickets/{id}")]
    public async Task<IActionResult> GetTicket(string id, CancellationToken ct = default)
    {
        var check = CheckPermission("dashboard.view");
        if (check is not null) return check;

        var dto = await Mediator.Send(new GetAdminTicketDetailQuery(id), ct);
        if (dto is null) return NotFound(new { success = false, error = new { code = "TICKET_NOT_FOUND" } });
        return Ok(new { success = true, data = dto });
    }

    [HttpPost("support/tickets/{id}/reply")]
    public async Task<IActionResult> ReplyToTicket(string id,
        [FromBody] TicketReplyRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("dashboard.view");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Body))
            return BadRequest(new { success = false, error = new { code = "BODY_REQUIRED" } });

        var result = await Mediator.Send(new ReplyToTicketCommand(
            id, req.Body, req.AttachmentsJson, req.InternalNote,
            StaffId, CurrentStaff.Email ?? StaffId), ct);

        if (!result.Success)
            return result.ErrorCode == "TICKET_NOT_FOUND"
                ? NotFound(new { success = false, error = new { code = result.ErrorCode } })
                : UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true, data = result.Data });
    }

    [HttpPost("support/tickets/{id}/assign")]
    public async Task<IActionResult> AssignTicket(string id,
        [FromBody] AssignTicketRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("dashboard.view");
        if (check is not null) return check;

        var assignToSelf = req.StaffId == "self";
        var result = await Mediator.Send(new AssignTicketCommand(
            id, assignToSelf ? null : req.StaffId, assignToSelf,
            StaffId, CurrentStaff.Email ?? StaffId), ct);

        if (!result.Success)
            return result.ErrorCode == "TICKET_NOT_FOUND"
                ? NotFound(new { success = false, error = new { code = result.ErrorCode } })
                : UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true });
    }

    [HttpPatch("support/tickets/{id}")]
    public async Task<IActionResult> UpdateTicket(string id,
        [FromBody] UpdateTicketRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("dashboard.view");
        if (check is not null) return check;

        var result = await Mediator.Send(new UpdateTicketCommand(
            id, req.Status, req.Priority, req.Category, req.ResolutionCode,
            StaffId, CurrentStaff.Email ?? StaffId), ct);

        if (!result.Success)
            return result.ErrorCode == "TICKET_NOT_FOUND"
                ? NotFound(new { success = false, error = new { code = result.ErrorCode } })
                : UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true });
    }

    [HttpPost("support/tickets/{id}/resolve-with")]
    public async Task<IActionResult> ResolveTicketWith(string id,
        [FromBody] ResolveWithRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("trips.refund");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        var result = await Mediator.Send(new ResolveTicketWithCommand(
            id, req.Action, req.Amount, req.Reason,
            StaffId, CurrentStaff.Email ?? StaffId), ct);

        if (!result.Success)
            return result.ErrorCode == "TICKET_NOT_FOUND"
                ? NotFound(new { success = false, error = new { code = result.ErrorCode } })
                : UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true });
    }

    [HttpGet("support/metrics")]
    public async Task<IActionResult> GetSupportMetrics(CancellationToken ct = default)
    {
        var check = CheckPermission("dashboard.view");
        if (check is not null) return check;

        return Ok(await Mediator.Send(new GetSupportMetricsQuery(Market), ct));
    }

    [HttpGet("support/canned-replies")]
    public async Task<IActionResult> GetCannedReplies(CancellationToken ct = default)
    {
        var check = CheckPermission("dashboard.view");
        if (check is not null) return check;

        return Ok(await Mediator.Send(new GetCannedRepliesQuery(Market), ct));
    }

    // ── A17: Broadcasts ───────────────────────────────────────────────────────

    [HttpGet("broadcasts")]
    public async Task<IActionResult> GetBroadcasts(
        [FromQuery] string? channel, [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int per_page = 25,
        CancellationToken ct = default)
    {
        var check = CheckPermission("dashboard.view");
        if (check is not null) return check;

        return Ok(await Mediator.Send(
            new GetAdminBroadcastsQuery(Market, channel, status, page, per_page), ct));
    }

    [HttpPost("broadcasts")]
    public async Task<IActionResult> CreateBroadcast(
        [FromBody] CreateBroadcastRequest req, CancellationToken ct = default)
    {
        // broadcasts.send is a dedicated permission — the spec is explicit about this
        var check = CheckPermission("broadcasts.send");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Title) || string.IsNullOrWhiteSpace(req.Body))
            return BadRequest(new { success = false, error = new { code = "TITLE_AND_BODY_REQUIRED" } });

        var result = await Mediator.Send(new CreateBroadcastCommand(
            req.Audience, req.Channel, req.Title, req.Body,
            req.DeepLink, req.ScheduledAt,
            Market, StaffId, CurrentStaff.Email ?? StaffId), ct);

        if (!result.Success)
            return UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true, data = result.Data });
    }

    [HttpPost("broadcasts/estimate")]
    public async Task<IActionResult> EstimateBroadcast(
        [FromBody] EstimateBroadcastRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("dashboard.view");
        if (check is not null) return check;

        var result = await Mediator.Send(
            new EstimateBroadcastCommand(req.Audience, req.Channel, Market), ct);

        return Ok(new { success = true, data = result.Data });
    }

    [HttpPost("broadcasts/{id}/cancel")]
    public async Task<IActionResult> CancelBroadcast(string id, CancellationToken ct = default)
    {
        var check = CheckPermission("broadcasts.send");
        if (check is not null) return check;

        var result = await Mediator.Send(new CancelBroadcastCommand(
            id, StaffId, CurrentStaff.Email ?? StaffId), ct);

        if (!result.Success)
            return result.ErrorCode == "BROADCAST_NOT_FOUND"
                ? NotFound(new { success = false, error = new { code = result.ErrorCode } })
                : UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true });
    }

    [HttpPost("broadcasts/test")]
    public async Task<IActionResult> TestBroadcast(
        [FromBody] TestBroadcastRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("broadcasts.send");
        if (check is not null) return check;

        var result = await Mediator.Send(new TestBroadcastCommand(
            req.Title, req.Body, req.DeepLink, req.Channel,
            StaffId, CurrentStaff.Email ?? StaffId), ct);

        if (!result.Success)
            return UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true, data = result.Data });
    }

    // ── A17: Reports ──────────────────────────────────────────────────────────

    [HttpGet("reports")]
    public async Task<IActionResult> GetReports(CancellationToken ct = default)
    {
        var check = CheckPermission("dashboard.view");
        if (check is not null) return check;

        return Ok(await Mediator.Send(new GetAdminReportsQuery(Market), ct));
    }

    [HttpPost("reports/{key}/run")]
    public async Task<IActionResult> RunReport(string key,
        [FromQuery] string? format, CancellationToken ct = default)
    {
        var check = CheckPermission("dashboard.view");
        if (check is not null) return check;

        var result = await Mediator.Send(new RunReportCommand(key, format, StaffId), ct);

        if (!result.Success)
            return result.ErrorCode == "REPORT_KEY_NOT_FOUND"
                ? NotFound(new { success = false, error = new { code = result.ErrorCode } })
                : UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } });

        return Accepted(new { success = true, data = result.Data });
    }

    [HttpPost("reports/schedules")]
    public async Task<IActionResult> ScheduleReport(
        [FromBody] ScheduleReportRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("dashboard.view");
        if (check is not null) return check;

        var result = await Mediator.Send(new ScheduleReportCommand(
            req.Key, req.Cron, req.Recipients ?? [], req.Format ?? "csv", StaffId), ct);

        return Ok(new { success = true, data = result.Data });
    }

    // ── A17: Jobs (shared by all 202 responses across the entire contract) ────

    [HttpGet("jobs/{id}")]
    public async Task<IActionResult> GetJob(string id, CancellationToken ct = default)
    {
        var check = CheckPermission("dashboard.view");
        if (check is not null) return check;

        var result = await Mediator.Send(new GetBackgroundJobQuery(id), ct);
        if (result is null) return NotFound(new { success = false, error = new { code = "JOB_NOT_FOUND" } });
        return Ok(result);
    }
}

// ── Request shapes ────────────────────────────────────────────────────────────

public record CreateCouponRequest(string Code, string Title, string DiscountType,
    long Value, long MaxDiscount, long MinOrder,
    string[]? Verticals, string[]? Zones,
    bool FirstTripOnly, int PerUserLimit, int TotalCap,
    DateTime? StartsAt, DateTime? ExpiresAt);

public record UpdateCouponRequest(string? Title, string? DiscountType, long? Value,
    long? MaxDiscount, long? MinOrder,
    string[]? Verticals, string[]? Zones,
    bool? FirstTripOnly, int? PerUserLimit, int? TotalCap,
    DateTime? StartsAt, DateTime? ExpiresAt, bool? IsActive);

public record UpdateReferralConfigRequest(long RewardReferrer, long RewardReferred,
    int QualificationTrips, long? Cap);

public record CreateIncentiveRequest(string Title, string Audience, object? Condition,
    long Reward, string? Currency, long? BudgetCap, DateTime? StartsAt, DateTime? EndsAt);

public record TicketReplyRequest(string Body, string? AttachmentsJson, bool InternalNote = false);
public record AssignTicketRequest(string StaffId);
public record UpdateTicketRequest(string? Status, string? Priority,
    string? Category, string? ResolutionCode);
public record ResolveWithRequest(string Action, long? Amount, string Reason);

public record CreateBroadcastRequest(string Audience, string Channel,
    string Title, string Body, string? DeepLink, DateTime? ScheduledAt);
public record EstimateBroadcastRequest(string Audience, string Channel);
public record TestBroadcastRequest(string Title, string Body, string? DeepLink, string Channel);
public record ScheduleReportRequest(string Key, string Cron, string[]? Recipients, string? Format);
