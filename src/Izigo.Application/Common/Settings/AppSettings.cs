namespace Izigo.Application.Common.Settings;

public class AppSettings
{
    public string ShareBaseUrl             { get; set; } = "https://izigo.app/t/";
    public string ApiBaseUrl               { get; set; } = "https://api.izigo.app/api/v1";
    public string SupportEmail             { get; set; } = "support@izigo.app";
    public string SupportPhone             { get; set; } = "+225 07 00 00 00 00";
    public string SupportWhatsapp          { get; set; } = "+225 07 00 00 00 00";
    public string SosNumber                { get; set; } = "+225 1717";
    public string SupportHours             { get; set; } = "Mon–Fri 8h–20h, Sat 9h–17h";
    public bool   LiveChatEnabled          { get; set; } = true;
    public long   ReferralRewardPerInvite  { get; set; } = 500;
    public int    ShareTokenExpiryHours    { get; set; } = 24;
    public int    CoRideMinDepartureMinutes { get; set; } = 10;
}
