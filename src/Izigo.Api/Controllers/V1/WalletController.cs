using Izigo.Application.Common.Models;
using Izigo.Application.Features.Wallets.Commands;
using Izigo.Application.Features.Wallets.Dtos;
using Izigo.Application.Features.Wallets.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Izigo.Api.Controllers.V1;

/// <summary>
/// Provides access to the rider's in-app wallet: balance, transaction history,
/// top-up, peer transfers, and withdrawals.
/// </summary>
[Route("api/v1/wallet")]
[Authorize(Policy = "RiderPolicy")]
public class WalletController : BaseController
{
    /// <summary>Returns the current wallet balance and status.</summary>
    [HttpGet("")]
    public async Task<IActionResult> GetWallet()
        => Ok(await Mediator.Send(new GetWalletQuery(CurrentUserId)));

    /// <summary>Returns a paginated list of wallet transactions.</summary>
    [HttpGet("transactions")]
    public async Task<IActionResult> GetTransactions(
        [FromQuery] string? type,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int per_page = 20)
    {
        var (items, total) = await Mediator.Send(
            new GetWalletTransactionsQuery(CurrentUserId, type, from, to, page, per_page));
        return Ok(items, PagedMeta.From(page, per_page, total));
    }

    /// <summary>Returns per-bucket spending totals for the Weekly Spending card.</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary([FromQuery] string period = "week")
        => Ok(await Mediator.Send(new GetWalletSummaryQuery(CurrentUserId, period)));

    /// <summary>Initiates a wallet top-up via the configured payment gateway.</summary>
    [HttpPost("topup")]
    public async Task<IActionResult> TopUp(
        [FromBody] TopUpRequest body,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
        => Ok(await Mediator.Send(new TopUpCommand(
            CurrentUserId, body with { IdempotencyKey = idempotencyKey ?? body.IdempotencyKey })));

    /// <summary>Looks up a recipient by phone number before initiating a transfer.</summary>
    [HttpPost("transfer/lookup")]
    public async Task<IActionResult> LookupTransferRecipient([FromBody] TransferLookupRequest body)
        => Ok(await Mediator.Send(new LookupTransferRecipientQuery(CurrentUserId, body)));

    /// <summary>Transfers funds from the rider's wallet to another user.</summary>
    [HttpPost("transfer")]
    public async Task<IActionResult> Transfer(
        [FromBody] TransferRequest body,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
        => Ok(await Mediator.Send(new TransferCommand(
            CurrentUserId, body with { IdempotencyKey = idempotencyKey ?? body.IdempotencyKey })));

    /// <summary>Initiates a withdrawal of wallet funds to an external account.</summary>
    [HttpPost("withdraw")]
    public async Task<IActionResult> Withdraw(
        [FromBody] WithdrawRequest body,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
        => Ok(await Mediator.Send(new WithdrawCommand(
            CurrentUserId, body with { IdempotencyKey = idempotencyKey ?? body.IdempotencyKey })));

    /// <summary>Returns the applicable transaction limits for the wallet.</summary>
    [HttpGet("limits")]
    public async Task<IActionResult> GetLimits()
        => Ok(await Mediator.Send(new GetWalletLimitsQuery(CurrentUserId)));
}
