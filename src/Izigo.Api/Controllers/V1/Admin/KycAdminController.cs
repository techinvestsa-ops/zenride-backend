using Izigo.Application.Features.Admin.Kyc.Commands;
using Izigo.Application.Features.Admin.Kyc.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Izigo.Api.Controllers.V1.Admin;

/// <summary>
/// Admin KYC review workflow (A09): queue, per-driver detail, signed document URLs,
/// per-step approve/reject, full approve/reject, reviewer assignment, metrics,
/// and document expiry watchlist.
/// </summary>
[Authorize(Policy = "AdminPolicy")]
public class KycAdminController : AdminBaseController
{
    // ── KYC Queue & Driver Review ─────────────────────────────────────────────

    /// <summary>Returns the KYC review queue ordered oldest-first. Each application includes all 8 steps with status, values, and documents.</summary>
    [HttpGet("api/v1/admin/kyc/queue")]
    public async Task<IActionResult> GetQueue(
        [FromQuery] string? q, [FromQuery] int page = 1,
        [FromQuery] int per_page = 25, CancellationToken ct = default)
    {
        var check = CheckPermission("kyc.view");
        if (check is not null) return check;
        return Ok(await Mediator.Send(new GetKycQueueQuery(q, page, per_page), ct));
    }

    /// <summary>Returns the full KYC application for a driver, including automated checks: face-match, duplicate device, plate collision.</summary>
    [HttpGet("api/v1/admin/kyc/{driverId}")]
    public async Task<IActionResult> GetDriverKyc(string driverId, CancellationToken ct = default)
    {
        var check = CheckPermission("kyc.view");
        if (check is not null) return check;

        var dto = await Mediator.Send(new GetKycDetailQuery(driverId), ct);
        if (dto is null)
            return NotFound(new { success = false, error = new { code = "APPLICATION_NOT_FOUND" } });
        return Ok(new { success = true, data = dto });
    }

    /// <summary>Returns a short-lived signed URL for a KYC document. Every access is audited.</summary>
    [HttpGet("api/v1/admin/kyc/{driverId}/documents/{docId}")]
    public async Task<IActionResult> GetDocument(string driverId, string docId, CancellationToken ct = default)
    {
        var check = CheckPermission("kyc.view");
        if (check is not null) return check;

        var dto = await Mediator.Send(
            new GetKycDocumentUrlQuery(driverId, docId, StaffId, CurrentStaff.Email ?? StaffId), ct);
        if (dto is null)
            return NotFound(new { success = false, error = new { code = "DOCUMENT_NOT_FOUND" } });
        return Ok(new { success = true, data = dto });
    }

    /// <summary>Approves a single KYC step for a driver. Permission: kyc.approve.</summary>
    [HttpPost("api/v1/admin/kyc/{driverId}/steps/{step}/approve")]
    public async Task<IActionResult> ApproveStep(string driverId, string step, CancellationToken ct = default)
    {
        var check = CheckPermission("kyc.approve");
        if (check is not null) return check;

        var result = await Mediator.Send(
            new ApproveKycStepCommand(driverId, step, StaffId, CurrentStaff.Email ?? StaffId), ct);

        if (!result.Success)
            return result.ErrorCode == "APPLICATION_NOT_FOUND"
                ? NotFound(new { success = false, error = new { code = result.ErrorCode } })
                : BadRequest(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true });
    }

    /// <summary>Rejects a single KYC step. The reason is shown in the driver app and pushes kyc.status_changed.</summary>
    [HttpPost("api/v1/admin/kyc/{driverId}/steps/{step}/reject")]
    public async Task<IActionResult> RejectStep(string driverId, string step,
        [FromBody] KycStepRejectRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("kyc.approve");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        var result = await Mediator.Send(
            new RejectKycStepCommand(driverId, step, req.Reason, StaffId, CurrentStaff.Email ?? StaffId), ct);

        if (!result.Success)
            return result.ErrorCode == "APPLICATION_NOT_FOUND"
                ? NotFound(new { success = false, error = new { code = result.ErrorCode } })
                : BadRequest(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true });
    }

    /// <summary>Approves the full KYC application. Returns 422 if any step is not yet approved.</summary>
    [HttpPost("api/v1/admin/kyc/{driverId}/approve")]
    public async Task<IActionResult> ApproveKyc(string driverId, CancellationToken ct = default)
    {
        var check = CheckPermission("kyc.approve");
        if (check is not null) return check;

        var result = await Mediator.Send(
            new ApproveKycApplicationCommand(driverId, StaffId, CurrentStaff.Email ?? StaffId), ct);

        if (!result.Success)
        {
            if (result.ErrorCode?.StartsWith("STEPS_NOT_APPROVED") == true)
            {
                var unapproved = result.ErrorCode.Split(":").ElementAtOrDefault(1)?.Split(",") ?? [];
                return UnprocessableEntity(new
                {
                    success = false,
                    error = new { code = "STEPS_NOT_APPROVED", unapproved_steps = unapproved }
                });
            }
            return NotFound(new { success = false, error = new { code = result.ErrorCode } });
        }

        return Ok(new { success = true });
    }

    /// <summary>Rejects the full KYC application. blocklist=true blocks the account for fraud.</summary>
    [HttpPost("api/v1/admin/kyc/{driverId}/reject")]
    public async Task<IActionResult> RejectKyc(string driverId,
        [FromBody] KycApplicationRejectRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("kyc.approve");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        var result = await Mediator.Send(new RejectKycApplicationCommand(
            driverId, req.Reason, req.Blocklist, StaffId, CurrentStaff.Email ?? StaffId), ct);

        return result.Success
            ? Ok(new { success = true })
            : NotFound(new { success = false, error = new { code = result.ErrorCode } });
    }

    /// <summary>Claims the KYC application for this reviewer, preventing two reviewers from working the same file.</summary>
    [HttpPost("api/v1/admin/kyc/{driverId}/assign")]
    public async Task<IActionResult> AssignReviewer(string driverId, CancellationToken ct = default)
    {
        var check = CheckPermission("kyc.view");
        if (check is not null) return check;

        var result = await Mediator.Send(
            new AssignKycReviewerCommand(driverId, StaffId, CurrentStaff.Email ?? StaffId), ct);

        if (!result.Success)
            return result.ErrorCode == "ALREADY_ASSIGNED"
                ? Conflict(new { success = false, error = new { code = result.ErrorCode } })
                : NotFound(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true });
    }

    /// <summary>Returns KYC queue health: depth, oldest wait, approvals per reviewer, rejection reasons ranked.</summary>
    [HttpGet("api/v1/admin/kyc/metrics")]
    public async Task<IActionResult> GetKycMetrics(CancellationToken ct = default)
    {
        var check = CheckPermission("kyc.view");
        if (check is not null) return check;
        return Ok(new { success = true, data = await Mediator.Send(new GetKycMetricsQuery(), ct) });
    }

    // ── Document Management ───────────────────────────────────────────────────

    /// <summary>Returns all documents expiring within the specified window. Drives the reminder push and auto-offline job.</summary>
    [HttpGet("api/v1/admin/documents/expiring")]
    public async Task<IActionResult> GetExpiringDocuments(
        [FromQuery] int within_days = 30, CancellationToken ct = default)
    {
        var check = CheckPermission("kyc.view");
        if (check is not null) return check;
        return Ok(await Mediator.Send(new GetExpiringDocumentsQuery(within_days), ct));
    }

    /// <summary>Reviews (approve or reject) a document renewal with an updated expiry date.</summary>
    [HttpPost("api/v1/admin/documents/{id}/review")]
    public async Task<IActionResult> ReviewDocument(string id,
        [FromBody] DocumentReviewRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("kyc.approve");
        if (check is not null) return check;

        if (req.Decision is not ("approve" or "reject"))
            return BadRequest(new { success = false, error = new { code = "INVALID_DECISION" } });

        var result = await Mediator.Send(new ReviewDocumentCommand(
            id, req.Decision, req.Reason, req.ExpiresAt, StaffId, CurrentStaff.Email ?? StaffId), ct);

        return result.Success
            ? Ok(new { success = true })
            : NotFound(new { success = false, error = new { code = result.ErrorCode } });
    }
}

// ── Request shapes ────────────────────────────────────────────────────────────

public record KycStepRejectRequest(string Reason);
public record KycApplicationRejectRequest(string Reason, bool Blocklist = false);
public record DocumentReviewRequest(string Decision, string? Reason, DateTime? ExpiresAt);
