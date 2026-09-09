namespace Izigo.Application.Common.Settings;

public class AdminAuthSettings
{
    public int TwoFaChallengeExpirySeconds    { get; set; } = 300;
    public int ForgotPasswordExpiryMinutes    { get; set; } = 30;
    public int RefreshExpiryHours             { get; set; } = 12;
    public int InviteExpiryHours              { get; set; } = 48;
    public int StaffPasswordResetExpiryHours  { get; set; } = 4;
}
