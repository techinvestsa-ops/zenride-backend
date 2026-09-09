namespace Izigo.Application.Common.Settings;

public class OpsSettings
{
    public double EtaSpeedKmh                  { get; set; } = 30.0;
    public int    DriverLocationFreshnessMinutes { get; set; } = 5;
    public double NearbyDriverFuzzDegrees       { get; set; } = 0.003;
    public int    SosTrailBeforeMinutes         { get; set; } = 5;
    public int    SosTrailAfterMinutes          { get; set; } = 30;
}
