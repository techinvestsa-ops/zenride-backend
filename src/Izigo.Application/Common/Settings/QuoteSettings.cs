namespace Izigo.Application.Common.Settings;

public class QuoteSettings
{
    public int     ExpiryMinutes        { get; set; } = 5;
    public double  FallbackSpeedMs      { get; set; } = 8.3;   // m/s ≈ 30 km/h
    public decimal ServiceFeeRate       { get; set; } = 0.08m;
    public long    DefaultCancellationFee { get; set; } = 500;
}
