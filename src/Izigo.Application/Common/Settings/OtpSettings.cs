namespace Izigo.Application.Common.Settings;

public class OtpSettings
{
    public int ExpirySeconds          { get; set; } = 300;
    public int EmailExpirySeconds     { get; set; } = 600;
    public int PasswordResetExpirySeconds { get; set; } = 600;
    public int ResendCooldownSeconds  { get; set; } = 60;
    public int MaxPerHour             { get; set; } = 5;
}
