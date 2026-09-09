namespace Izigo.Domain.Common;

/// <summary>
/// Generates time-ordered, prefixed string IDs.
/// Uses Guid.CreateVersion7() so IDs sort chronologically.
/// Entities that appear in API responses use the prefixes shown in the contract (usr_, trp_, etc.).
/// </summary>
public static class EntityId
{
    private static string Base() =>
        Guid.CreateVersion7().ToString("N").ToLower()[..20];

    // ── Core domain objects (contract-visible prefixes) ──────────────────────
    public static string ForUser()              => $"usr_{Base()}";
    public static string ForTrip()              => $"trp_{Base()}";
    public static string ForQuote()             => $"qt_{Base()}";
    public static string ForCoRideListing()     => $"crl_{Base()}";
    public static string ForCoRideBooking()     => $"bkg_{Base()}";
    public static string ForPackage()           => $"pkg_{Base()}";
    public static string ForDriver()            => $"drv_{Base()}";
    public static string ForWallet()            => $"wlt_{Base()}";
    public static string ForDriverWallet()      => $"dwl_{Base()}";
    public static string ForPayment()           => $"pay_{Base()}";
    public static string ForPayout()            => $"pyo_{Base()}";
    public static string ForSosIncident()       => $"sos_{Base()}";
    public static string ForTicket()            => $"tkt_{Base()}";
    public static string ForConversation()      => $"cnv_{Base()}";
    public static string ForNotification()      => $"ntf_{Base()}";
    public static string ForStaff()             => $"stf_{Base()}";
    public static string ForAuditLog()          => $"aud_{Base()}";
    public static string ForBackgroundJob()     => $"job_{Base()}";
    public static string ForZone()              => $"zn_{Base()}";
    public static string ForCoupon()            => $"cpn_{Base()}";
    public static string ForBroadcast()         => $"brd_{Base()}";
    public static string ForIncentive()         => $"inc_{Base()}";
    public static string ForFareRule()          => $"fr_{Base()}";

    // ── Supporting records (internal, still prefixed for traceability) ────────
    public static string ForDevice()                => $"dev_{Base()}";
    public static string ForRefreshToken()          => $"rt_{Base()}";
    public static string ForStaffRefreshToken()     => $"srt_{Base()}";
    public static string ForBiometricToken()        => $"bio_{Base()}";
    public static string ForSavedPlace()            => $"pl_{Base()}";
    public static string ForEmergencyContact()      => $"ec_{Base()}";
    public static string ForUserPreference()        => $"prf_{Base()}";
    public static string ForOtpRecord()             => $"otp_{Base()}";
    public static string ForIdempotency()           => $"idm_{Base()}";
    public static string ForDriverOnboarding()      => $"kyc_{Base()}";
    public static string ForDriverDocument()        => $"doc_{Base()}";
    public static string ForVehicle()               => $"veh_{Base()}";
    public static string ForWalletTransaction()     => $"wtx_{Base()}";
    public static string ForDriverWalletTx()        => $"dtx_{Base()}";
    public static string ForTripStateHistory()      => $"tsh_{Base()}";
    public static string ForCancellationReason()    => $"cr_{Base()}";
    public static string ForCoRideRequest()         => $"crq_{Base()}";
    public static string ForDeviceDiagnostic()      => $"ddi_{Base()}";
    public static string ForUserPaymentMethod()      => $"pm_{Base()}";
    public static string ForMessage()               => $"msg_{Base()}";
    public static string ForSupportTicketMessage()  => $"tmg_{Base()}";

    // Admin A1 additions
    public static string For2FaChallenge()          => $"tfc_{Base()}";
    public static string For2FaRecoveryCode()       => $"trc_{Base()}";
    public static string ForStaffInvite()           => $"inv_{Base()}";
    public static string ForStaffPasswordReset()    => $"prs_{Base()}";
    public static string ForDispatchConfig()        => $"dsc_{Base()}";
    public static string ForFeatureFlag()           => $"flg_{Base()}";
    public static string ForIntegration()           => $"itg_{Base()}";
    public static string ForRoleCustomization()     => $"rlc_{Base()}";
}
