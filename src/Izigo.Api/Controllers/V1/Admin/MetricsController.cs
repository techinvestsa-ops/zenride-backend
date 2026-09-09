using Izigo.Application.Features.Admin.Metrics.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Izigo.Api.Controllers.V1.Admin;

/// <summary>
/// Provides high-level business intelligence metrics: KPIs, GMV trends,
/// vertical mix, supply/demand signals, system alerts, conversion funnels,
/// and cohort analysis.
/// </summary>
[Route("api/v1/admin/metrics")]
[Authorize(Policy = "AdminPolicy")]
public class MetricsController : AdminBaseController
{
    /// <summary>Returns the six KPI tiles with value, delta_pct, note, and sparkline.</summary>
    [HttpGet("kpis")]
    public async Task<IActionResult> GetKpis([FromQuery] int range = 7, CancellationToken ct = default)
    {
        var check = CheckPermission("dashboard.view");
        if (check is not null) return check;
        return Ok(await Mediator.Send(new GetKpisQuery(Market, range), ct));
    }

    /// <summary>Returns a time-series breakdown of gross merchandise value by day.</summary>
    [HttpGet("gmv-series")]
    public async Task<IActionResult> GetGmvSeries([FromQuery] int days = 14, CancellationToken ct = default)
    {
        var check = CheckPermission("dashboard.view");
        if (check is not null) return check;
        return Ok(await Mediator.Send(new GetGmvSeriesQuery(Market, days), ct));
    }

    /// <summary>Returns trip volume split across verticals by day.</summary>
    [HttpGet("vertical-mix")]
    public async Task<IActionResult> GetVerticalMix([FromQuery] int? days, CancellationToken ct = default)
    {
        var check = CheckPermission("dashboard.view");
        if (check is not null) return check;
        return Ok(await Mediator.Send(new GetVerticalMixQuery(Market, days), ct));
    }

    /// <summary>Returns supply vs demand in 24 hourly buckets for today.</summary>
    [HttpGet("supply-demand")]
    public async Task<IActionResult> GetSupplyDemand(CancellationToken ct = default)
    {
        var check = CheckPermission("dashboard.view");
        if (check is not null) return check;
        return Ok(await Mediator.Send(new GetSupplyDemandQuery(Market), ct));
    }

    /// <summary>Returns server-computed operational alerts (KYC backlog, failed payouts, open SOS, etc.).</summary>
    [HttpGet("alerts")]
    public async Task<IActionResult> GetAlerts(CancellationToken ct = default)
    {
        var check = CheckPermission("dashboard.view");
        if (check is not null) return check;
        return Ok(await Mediator.Send(new GetAlertsQuery(Market), ct));
    }

    /// <summary>Returns conversion funnel data (signup→trip, request→completed, KYC drop-off).</summary>
    [HttpGet("funnels")]
    public async Task<IActionResult> GetFunnels(CancellationToken ct = default)
    {
        var check = CheckPermission("dashboard.view");
        if (check is not null) return check;
        return Ok(await Mediator.Send(new GetFunnelsQuery(Market), ct));
    }

    /// <summary>Returns signup cohort retention — trips per user by weeks since signup.</summary>
    [HttpGet("cohorts")]
    public async Task<IActionResult> GetCohorts(CancellationToken ct = default)
    {
        var check = CheckPermission("dashboard.view");
        if (check is not null) return check;
        return Ok(await Mediator.Send(new GetCohortsQuery(Market), ct));
    }
}
