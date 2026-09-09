namespace Izigo.Application.Common.Settings;

public class DispatchSettings
{
    public int    OfferTimeoutSeconds   { get; set; } = 15;
    public int    SearchRadiusM         { get; set; } = 3000;
    public string Strategy              { get; set; } = "broadcast";
    public int    MaxConcurrentOffers   { get; set; } = 3;
    public int    LocationPingOnTripS   { get; set; } = 5;
    public int    LocationPingIdleS     { get; set; } = 20;
}
