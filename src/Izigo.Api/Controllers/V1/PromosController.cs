using Izigo.Application.Features.Promotions.Commands;
using Izigo.Application.Features.Promotions.Dtos;
using Izigo.Application.Features.Promotions.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Izigo.Api.Controllers.V1;

/// <summary>
/// Handles promotional features: coupon listing and validation, and
/// referral code redemption.
/// </summary>
[Route("api/v1")]
[Authorize(Policy = "RiderPolicy")]
public class PromosController : BaseController
{
    /// <summary>Returns all coupons available or already claimed by the rider.</summary>
    [HttpGet("coupons")]
    public async Task<IActionResult> GetCoupons()
        => Ok(await Mediator.Send(new GetCouponsQuery(CurrentUserId)));

    /// <summary>Validates a coupon code and returns its discount details.</summary>
    [HttpPost("coupons/validate")]
    public async Task<IActionResult> ValidateCoupon([FromBody] ValidateCouponRequest body)
        => Ok(await Mediator.Send(new ValidateCouponCommand(CurrentUserId, body)));

    /// <summary>Redeems a referral code and credits the associated reward.</summary>
    [HttpPost("referrals/redeem")]
    public async Task<IActionResult> RedeemReferral([FromBody] RedeemReferralRequest body)
        => Ok(await Mediator.Send(new RedeemReferralCommand(CurrentUserId, body)));
}
