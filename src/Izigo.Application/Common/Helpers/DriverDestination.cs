namespace Izigo.Application.Common.Helpers;

/// <summary>
/// The driver destination filter's rule, shared by dispatch and driver claims:
/// a trip fits when its drop-off brings the driver clearly closer to their
/// destination and its pickup is not a detour away from it.
/// </summary>
public static class DriverDestination
{
    public const int MaxUsesPerDay = 2;

    /// <summary>The drop-off must be at least this much closer than the driver is now (0.7 = 30% closer).</summary>
    private const double CloserFactor = 0.7;

    /// <summary>How much further from the destination the pickup may be than the driver is now.</summary>
    private const double PickupSlackM = 2_000;

    /// <summary>Within this of the destination the driver has arrived and the filter ends.</summary>
    public const double ArrivedM = 1_000;

    public static bool Fits(
        double driverLat, double driverLng,
        double destLat, double destLng,
        double pickupLat, double pickupLng,
        double dropoffLat, double dropoffLng)
    {
        var now = Metres(driverLat, driverLng, destLat, destLng);
        var pickup = Metres(pickupLat, pickupLng, destLat, destLng);
        var after = Metres(dropoffLat, dropoffLng, destLat, destLng);
        return after <= now * CloserFactor && pickup <= now + PickupSlackM;
    }

    public static double Metres(double lat1, double lng1, double lat2, double lng2)
    {
        const double r = 6_371_000;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLng = (lng2 - lng1) * Math.PI / 180;
        var h = Math.Pow(Math.Sin(dLat / 2), 2) +
                Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Pow(Math.Sin(dLng / 2), 2);
        return 2 * r * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }
}
