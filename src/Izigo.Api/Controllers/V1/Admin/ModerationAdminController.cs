using Izigo.Application.Features.Admin.Moderation.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Izigo.Api.Controllers.V1.Admin;

/// <summary>
/// Moderation log (A20): a filtered view of the audit log showing who was blocked,
/// suspended, flagged, or had their sessions revoked — and by whom and why.
/// </summary>
[Authorize(Policy = "AdminPolicy")]
public class ModerationAdminController : AdminBaseController
{
    /// <summary>
    /// Returns moderation actions (blocks, suspensions, flags, session revocations)
    /// from the audit log. Filter by actor, target_type, action, date.
    /// </summary>
    [HttpGet("api/v1/admin/moderation/log")]
    public async Task<IActionResult> GetModerationLog(
        [FromQuery] string? actor,
        [FromQuery] string? target_type,
        [FromQuery] string? action,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int per_page = 25,
        CancellationToken ct = default)
    {
        var check = CheckPermission("moderation.view");
        if (check is not null) return check;

        return Ok(await Mediator.Send(new GetModerationLogQuery(
            actor, target_type, action, from, to, page, per_page), ct));
    }
}
