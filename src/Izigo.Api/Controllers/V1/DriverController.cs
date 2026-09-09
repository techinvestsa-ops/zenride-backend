using Izigo.Application.Common.Models;
using Izigo.Application.Features.CoRide.Commands;
using Izigo.Application.Features.Promotions.Queries;
using Izigo.Application.Features.CoRide.Dtos;
using Izigo.Application.Features.CoRide.Queries;
using Izigo.Application.Features.Driver.Commands;
using Izigo.Application.Features.Driver.Dtos;
using Izigo.Application.Features.Driver.Queries;
using Izigo.Application.Features.Packages.Commands;
using Izigo.Application.Features.Packages.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Izigo.Api.Controllers.V1;

/// <summary>
/// Comprehensive driver-side API covering onboarding and KYC, status management,
/// job dispatch, earnings, wallet, performance, vehicles, documents, preferences,
/// package delivery, co-ride listing management, and incentives.
/// </summary>
[Route("api/v1/driver")]
[Authorize(Policy = "DriverPolicy")]
public class DriverController : BaseController
{
    // ── Onboarding ────────────────────────────────────────────────────────────

    /// <summary>Returns the driver's current onboarding progress and step statuses.</summary>
    [HttpGet("onboarding")]
    public async Task<IActionResult> GetOnboarding()
        => Ok(await Mediator.Send(new GetOnboardingQuery(CurrentUserId)));

    /// <summary>Submits the driver's personal information step.</summary>
    [HttpPost("onboarding/personal")]
    public async Task<IActionResult> SubmitPersonal([FromBody] SubmitPersonalRequest body)
        => Ok(await Mediator.Send(new SubmitPersonalCommand(CurrentUserId, body)));

    /// <summary>Submits the driver's national identity document.</summary>
    [HttpPost("onboarding/identity")]
    public async Task<IActionResult> SubmitIdentity([FromBody] SubmitIdentityRequest body)
        => Ok(await Mediator.Send(new SubmitIdentityCommand(CurrentUserId, body)));

    /// <summary>Submits the driver's driving licence details and images.</summary>
    [HttpPost("onboarding/license")]
    public async Task<IActionResult> SubmitLicense([FromBody] SubmitLicenseRequest body)
        => Ok(await Mediator.Send(new SubmitLicenseCommand(CurrentUserId, body)));

    /// <summary>Submits the vehicle registration and details for the onboarding flow.</summary>
    [HttpPost("onboarding/vehicle")]
    public async Task<IActionResult> SubmitVehicle([FromBody] SubmitVehicleRequest body)
        => Ok(await Mediator.Send(new SubmitVehicleCommand(CurrentUserId, body)));

    /// <summary>Submits the vehicle insurance certificate.</summary>
    [HttpPost("onboarding/insurance")]
    public async Task<IActionResult> SubmitInsurance([FromBody] SubmitInsuranceRequest body)
        => Ok(await Mediator.Send(new SubmitInsuranceCommand(CurrentUserId, body)));

    /// <summary>Submits the driver's guarantor information.</summary>
    [HttpPost("onboarding/guarantor")]
    public async Task<IActionResult> SubmitGuarantor([FromBody] SubmitGuarantorRequest body)
        => Ok(await Mediator.Send(new SubmitGuarantorCommand(CurrentUserId, body)));

    /// <summary>Submits the driver's payout / bank account details.</summary>
    [HttpPost("onboarding/payout")]
    public async Task<IActionResult> SubmitPayout([FromBody] SubmitPayoutRequest body)
        => Ok(await Mediator.Send(new SubmitPayoutCommand(CurrentUserId, body)));

    /// <summary>Submits the driver's live selfie for identity verification.</summary>
    [HttpPost("onboarding/selfie")]
    public async Task<IActionResult> SubmitSelfie([FromBody] SubmitSelfieRequest body)
        => Ok(await Mediator.Send(new SubmitSelfieCommand(CurrentUserId, body)));

    /// <summary>Finalises and submits the completed onboarding application for review.</summary>
    [HttpPost("onboarding/submit")]
    public async Task<IActionResult> SubmitOnboarding()
        => Ok(await Mediator.Send(new SubmitOnboardingCommand(CurrentUserId)));

    // ── KYC ──────────────────────────────────────────────────────────────────

    /// <summary>Returns the driver's current KYC status and document verification results.</summary>
    [HttpGet("kyc")]
    public async Task<IActionResult> GetKyc()
        => Ok(await Mediator.Send(new GetKycQuery(CurrentUserId)));

    // ── Banks & Payout ────────────────────────────────────────────────────────

    /// <summary>Returns the list of supported banks for payout account setup.</summary>
    [HttpGet("banks")]
    public async Task<IActionResult> GetBanks()
        => Ok(await Mediator.Send(new GetBanksQuery("ci")));

    /// <summary>Resolves and validates a bank account number before linking it.</summary>
    [HttpPost("payout/resolve-account")]
    public async Task<IActionResult> ResolvePayoutAccount([FromBody] ResolveAccountRequest body)
        => Ok(await Mediator.Send(new ResolveAccountQuery(body)));

    // ── Status ────────────────────────────────────────────────────────────────

    /// <summary>Updates the driver's online/offline status.</summary>
    [HttpPost("status")]
    public async Task<IActionResult> SetStatus([FromBody] SetStatusRequest body)
        => Ok(await Mediator.Send(new SetDriverStatusCommand(CurrentUserId, body)));

    /// <summary>Returns the driver's current online/offline status.</summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
        => Ok(await Mediator.Send(new GetDriverStatusQuery(CurrentUserId)));

    // ── Location ──────────────────────────────────────────────────────────────

    /// <summary>Updates the driver's real-time GPS location.</summary>
    [HttpPost("location")]
    public async Task<IActionResult> UpdateLocation([FromBody] UpdateLocationRequest body)
    {
        await Mediator.Send(new UpdateLocationCommand(CurrentUserId, body));
        return NoContent();
    }

    // ── Home & Demand ─────────────────────────────────────────────────────────

    /// <summary>Returns the driver's home-screen summary (earnings today, nearby demand, etc.).</summary>
    [HttpGet("home")]
    public async Task<IActionResult> GetHome()
        => Ok(await Mediator.Send(new GetDriverHomeQuery(CurrentUserId)));

    /// <summary>Returns demand heatmap data for the driver's area.</summary>
    [HttpGet("demand")]
    public async Task<IActionResult> GetDemand()
        => Ok(await Mediator.Send(new GetDemandQuery(CurrentUserId)));

    // ── Job Dispatch ──────────────────────────────────────────────────────────

    /// <summary>Returns the current pending job offer waiting for the driver's response.</summary>
    [HttpGet("jobs/offer")]
    public async Task<IActionResult> GetJobOffer()
    {
        var result = await Mediator.Send(new GetJobOfferQuery(CurrentUserId));
        if (result == null) return NoContent();
        return Ok(result);
    }

    /// <summary>Accepts a dispatched job offer.</summary>
    [HttpPost("jobs/{id}/accept")]
    public async Task<IActionResult> AcceptJob(string id)
        => Ok(await Mediator.Send(new AcceptJobCommand(CurrentUserId, id)));

    /// <summary>Declines a dispatched job offer.</summary>
    [HttpPost("jobs/{id}/decline")]
    public async Task<IActionResult> DeclineJob(string id, [FromBody] DeclineJobRequest? body)
    {
        await Mediator.Send(new DeclineJobCommand(CurrentUserId, id, body?.Reason));
        return NoContent();
    }

    /// <summary>Returns the driver's currently active (in-progress) job.</summary>
    [HttpGet("jobs/active")]
    public async Task<IActionResult> GetActiveJob()
    {
        var result = await Mediator.Send(new GetActiveJobQuery(CurrentUserId));
        if (result == null) return NoContent();
        return Ok(result);
    }

    /// <summary>Marks the driver as en-route: Accepted→EnRouteToPickup or PickedUp→EnRouteToDropoff.</summary>
    [HttpPost("jobs/{id}/en-route")]
    public async Task<IActionResult> EnRoute(string id)
        => Ok(await Mediator.Send(new EnRouteCommand(CurrentUserId, id)));

    /// <summary>Records that the driver has arrived at the pickup location.</summary>
    [HttpPost("jobs/{id}/arrived-pickup")]
    public async Task<IActionResult> ArrivedAtPickup(string id)
        => Ok(await Mediator.Send(new ArrivedPickupCommand(CurrentUserId, id)));

    /// <summary>Starts the trip / marks the rider as on board.</summary>
    [HttpPost("jobs/{id}/start")]
    public async Task<IActionResult> StartJob(string id, [FromBody] StartJobRequest body)
        => Ok(await Mediator.Send(new StartJobCommand(CurrentUserId, id, body.Otp)));

    /// <summary>Records that the driver has arrived at the drop-off location.</summary>
    [HttpPost("jobs/{id}/arrived-dropoff")]
    public async Task<IActionResult> ArrivedAtDropoff(string id)
        => Ok(await Mediator.Send(new ArrivedDropoffCommand(CurrentUserId, id)));

    /// <summary>Completes the trip and triggers billing.</summary>
    [HttpPost("jobs/{id}/complete")]
    public async Task<IActionResult> CompleteJob(string id)
        => Ok(await Mediator.Send(new CompleteJobCommand(CurrentUserId, id)));

    /// <summary>Cancels an accepted job.</summary>
    [HttpPost("jobs/{id}/cancel")]
    public async Task<IActionResult> CancelJob(string id, [FromBody] CancelJobRequest? body)
    {
        await Mediator.Send(new CancelJobCommand(CurrentUserId, id, body?.Reason));
        return NoContent();
    }

    /// <summary>Submits a rating for the customer after trip completion.</summary>
    [HttpPost("jobs/{id}/rate-customer")]
    public async Task<IActionResult> RateCustomer(string id, [FromBody] RateCustomerRequest body)
    {
        await Mediator.Send(new RateCustomerCommand(CurrentUserId, id, body.Stars, body.Comment));
        return NoContent();
    }

    /// <summary>Returns a paginated list of the driver's completed jobs.</summary>
    [HttpGet("jobs")]
    public async Task<IActionResult> GetJobs(
        [FromQuery] string? vertical,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int per_page = 20)
    {
        var (items, total) = await Mediator.Send(
            new GetJobsQuery(CurrentUserId, vertical, from, to, page, per_page));
        return Ok(items, PagedMeta.From(page, per_page, total));
    }

    /// <summary>Returns the details of a specific job.</summary>
    [HttpGet("jobs/{id}")]
    public async Task<IActionResult> GetJob(string id)
        => Ok(await Mediator.Send(new GetJobQuery(CurrentUserId, id)));

    /// <summary>Starts or extends the waiting timer at a pickup location.</summary>
    [HttpPost("jobs/{id}/waiting")]
    public async Task<IActionResult> StartWaiting(string id)
    {
        var result = await Mediator.Send(new StartWaitingCommand(CurrentUserId, id));
        return Ok(new { success = true, data = result });
    }

    // ── Earnings ──────────────────────────────────────────────────────────────

    /// <summary>Returns the driver's earnings summary for a configurable period.</summary>
    [HttpGet("earnings")]
    public async Task<IActionResult> GetEarnings([FromQuery] string period = "week")
        => Ok(await Mediator.Send(new GetEarningsQuery(CurrentUserId, period)));

    /// <summary>Returns a time-series breakdown of earnings (daily/weekly/monthly).</summary>
    [HttpGet("earnings/series")]
    public async Task<IActionResult> GetEarningsSeries(
        [FromQuery] string granularity = "day", [FromQuery] int days = 30)
        => Ok(await Mediator.Send(new GetEarningsSeriesQuery(CurrentUserId, granularity, days)));

    /// <summary>Returns a list of individual trips contributing to earnings.</summary>
    [HttpGet("earnings/trips")]
    public async Task<IActionResult> GetEarningsTrips(
        [FromQuery] int page = 1, [FromQuery] int per_page = 20)
    {
        var (items, total) = await Mediator.Send(
            new GetEarningsTripsQuery(CurrentUserId, page, per_page));
        return Ok(items, PagedMeta.From(page, per_page, total));
    }

    // ── Driver Wallet ─────────────────────────────────────────────────────────

    /// <summary>Returns the driver's wallet balance and status.</summary>
    [HttpGet("wallet")]
    public async Task<IActionResult> GetWallet()
        => Ok(await Mediator.Send(new GetDriverWalletQuery(CurrentUserId)));

    /// <summary>Returns a paginated list of the driver's wallet transactions.</summary>
    [HttpGet("wallet/transactions")]
    public async Task<IActionResult> GetWalletTransactions(
        [FromQuery] int page = 1, [FromQuery] int per_page = 20)
    {
        var (items, total) = await Mediator.Send(
            new GetDriverWalletTransactionsQuery(CurrentUserId, page, per_page));
        return Ok(items, PagedMeta.From(page, per_page, total));
    }

    /// <summary>Initiates a withdrawal from the driver's wallet to their payout account.</summary>
    [HttpPost("wallet/withdraw")]
    public async Task<IActionResult> WithdrawWallet(
        [FromBody] WithdrawEarningsRequest body,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
        => Ok(await Mediator.Send(new WithdrawEarningsCommand(
            CurrentUserId, body with { IdempotencyKey = idempotencyKey ?? body.IdempotencyKey })));

    // ── Payout Methods ────────────────────────────────────────────────────────

    /// <summary>Returns all saved payout methods for the driver.</summary>
    [HttpGet("payout-methods")]
    public async Task<IActionResult> GetPayoutMethods()
        => Ok(await Mediator.Send(new GetPayoutMethodsQuery(CurrentUserId)));

    /// <summary>Adds a new payout method (bank account or mobile money).</summary>
    [HttpPost("payout-methods")]
    public async Task<IActionResult> AddPayoutMethod([FromBody] AddPayoutMethodRequest body)
        => Ok(await Mediator.Send(new AddPayoutMethodCommand(CurrentUserId, body)));

    // ── Cash Settlement ───────────────────────────────────────────────────────

    /// <summary>Returns the driver's outstanding cash settlement balance.</summary>
    [HttpGet("cash-settlement")]
    public async Task<IActionResult> GetCashSettlement()
        => Ok(await Mediator.Send(new GetCashSettlementQuery(CurrentUserId)));

    /// <summary>Records a cash settlement payment made by the driver.</summary>
    [HttpPost("cash-settlement/pay")]
    public async Task<IActionResult> PayCashSettlement([FromBody] PayCashSettlementRequest body)
        => Ok(await Mediator.Send(new PayCashSettlementCommand(CurrentUserId, body)));

    /// <summary>Submits a wallet dispute or discrepancy report.</summary>
    [HttpPost("wallet/dispute")]
    public async Task<IActionResult> DisputeWallet([FromBody] DisputeWalletRequest body)
    {
        await Mediator.Send(new DisputeWalletCommand(CurrentUserId, body));
        return NoContent();
    }

    // ── Performance & Insights ────────────────────────────────────────────────

    /// <summary>Returns the driver's performance metrics (acceptance rate, completion rate, etc.).</summary>
    [HttpGet("performance")]
    public async Task<IActionResult> GetPerformance([FromQuery] string period = "week")
        => Ok(await Mediator.Send(new GetPerformanceQuery(CurrentUserId, period)));

    /// <summary>Returns peak-hour demand insights for the driver's zone.</summary>
    [HttpGet("insights/peak-hours")]
    public async Task<IActionResult> GetPeakHours()
        => Ok(await Mediator.Send(new GetPeakHoursQuery(CurrentUserId)));

    /// <summary>Returns the driver's earned badges and achievement progress.</summary>
    [HttpGet("badges")]
    public async Task<IActionResult> GetBadges()
        => Ok(await Mediator.Send(new GetBadgesQuery(CurrentUserId)));

    /// <summary>Returns the driver's recent customer ratings and feedback.</summary>
    [HttpGet("ratings")]
    public async Task<IActionResult> GetRatings(
        [FromQuery] int page = 1, [FromQuery] int per_page = 20)
    {
        var (items, total) = await Mediator.Send(
            new GetDriverRatingsQuery(CurrentUserId, page, per_page));
        return Ok(items, PagedMeta.From(page, per_page, total));
    }

    // ── Vehicles ──────────────────────────────────────────────────────────────

    /// <summary>Returns all vehicles registered under the driver's account.</summary>
    [HttpGet("vehicles")]
    public async Task<IActionResult> GetVehicles()
        => Ok(await Mediator.Send(new GetVehiclesQuery(CurrentUserId)));

    /// <summary>Registers a new vehicle for the driver.</summary>
    [HttpPost("vehicles")]
    public async Task<IActionResult> AddVehicle([FromBody] AddVehicleRequest body)
        => Ok(await Mediator.Send(new AddVehicleCommand(CurrentUserId, body)));

    /// <summary>Updates the details of a specific vehicle.</summary>
    [HttpPatch("vehicles/{id}")]
    public async Task<IActionResult> UpdateVehicle(string id, [FromBody] UpdateVehicleRequest body)
        => Ok(await Mediator.Send(new UpdateVehicleCommand(CurrentUserId, id, body)));

    // ── Documents ─────────────────────────────────────────────────────────────

    /// <summary>Returns all compliance documents on file for the driver.</summary>
    [HttpGet("documents")]
    public async Task<IActionResult> GetDocuments()
        => Ok(await Mediator.Send(new GetDocumentsQuery(CurrentUserId)));

    /// <summary>Uploads a new compliance document or renews an expiring one.</summary>
    [HttpPost("documents")]
    public async Task<IActionResult> UploadDocument([FromBody] UploadDocumentRequest body)
        => Ok(await Mediator.Send(new UploadDocumentCommand(CurrentUserId, body)));

    // ── Preferences ───────────────────────────────────────────────────────────

    /// <summary>Returns the driver's current preference settings.</summary>
    [HttpGet("preferences")]
    public async Task<IActionResult> GetPreferences()
        => Ok(await Mediator.Send(new GetDriverPreferencesQuery(CurrentUserId)));

    /// <summary>Updates one or more driver preference settings.</summary>
    [HttpPatch("preferences")]
    public async Task<IActionResult> UpdatePreferences([FromBody] UpdateDriverPreferencesRequest body)
        => Ok(await Mediator.Send(new UpdateDriverPreferencesCommand(CurrentUserId, body)));

    // ── Package Delivery ──────────────────────────────────────────────────────

    /// <summary>Uploads proof of delivery (photo) for a completed package job.</summary>
    [HttpPost("packages/{id}/proof")]
    public async Task<IActionResult> UploadDeliveryProof(string id, [FromBody] UploadProofRequest body)
        => Ok(await Mediator.Send(new UploadProofCommand(CurrentUserId, id, body.ProofCode, body.PhotoUrl)));

    /// <summary>Reports a failed delivery attempt for a package job.</summary>
    [HttpPost("packages/{id}/failed-delivery")]
    public async Task<IActionResult> ReportFailedDelivery(string id, [FromBody] FailedDeliveryRequest body)
    {
        await Mediator.Send(new ReportFailedDeliveryCommand(CurrentUserId, id, body.Reason));
        return NoContent();
    }

    // ── Co-Ride Listings ──────────────────────────────────────────────────────

    /// <summary>Creates a new co-ride listing for other riders to join.</summary>
    [HttpPost("co-ride/listings")]
    public async Task<IActionResult> CreateCoRideListing([FromBody] PublishListingRequest body)
        => Ok(await Mediator.Send(new PublishListingCommand(CurrentUserId, body)));

    /// <summary>Returns all co-ride listings created by the driver.</summary>
    [HttpGet("co-ride/listings")]
    public async Task<IActionResult> GetCoRideListings()
        => Ok(await Mediator.Send(new GetDriverListingsQuery(CurrentUserId)));

    /// <summary>Updates a co-ride listing (seats, route, schedule).</summary>
    [HttpPatch("co-ride/listings/{id}")]
    public async Task<IActionResult> UpdateCoRideListing(string id, [FromBody] EditListingRequest body)
        => Ok(await Mediator.Send(new EditListingCommand(CurrentUserId, id, body)));

    /// <summary>Cancels and removes a co-ride listing.</summary>
    [HttpDelete("co-ride/listings/{id}")]
    public async Task<IActionResult> DeleteCoRideListing(string id, [FromBody] DeleteListingRequest? body)
    {
        await Mediator.Send(new DeleteListingCommand(CurrentUserId, id, body?.Reason));
        return NoContent();
    }

    /// <summary>Returns the passenger manifest for a co-ride listing.</summary>
    [HttpGet("co-ride/listings/{id}/manifest")]
    public async Task<IActionResult> GetCoRideManifest(string id)
        => Ok(await Mediator.Send(new GetManifestQuery(CurrentUserId, id)));

    /// <summary>Records that a passenger has boarded for a co-ride booking.</summary>
    [HttpPost("co-ride/bookings/{id}/board")]
    public async Task<IActionResult> BoardCoRidePassenger(string id)
    {
        await Mediator.Send(new BoardPassengerCommand(CurrentUserId, id));
        return NoContent();
    }

    // ── Incentives ────────────────────────────────────────────────────────────

    /// <summary>Returns active incentive programs and the driver's progress toward them.</summary>
    [HttpGet("incentives")]
    public async Task<IActionResult> GetIncentives()
        => Ok(await Mediator.Send(new GetIncentivesQuery(CurrentUserId)));

    // ── Inline request records ────────────────────────────────────────────────

    public record DeclineJobRequest(string? Reason);
    public record StartJobRequest(string Otp);
    public record CancelJobRequest(string? Reason);
    public record RateCustomerRequest(int Stars, string? Comment);
    public record UploadProofRequest(string? ProofCode, string? PhotoUrl);
    public record FailedDeliveryRequest(string Reason);
}
