namespace Izigo.Application.Common;

/// <summary>Branded HTML email templates for all transactional emails sent by the platform.</summary>
public static class EmailTemplates
{
    // ── Layout helpers ─────────────────────────────────────────────────────────

    private static string Wrap(string title, string body) => $"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="UTF-8">
          <meta name="viewport" content="width=device-width,initial-scale=1.0">
          <title>{title}</title>
        </head>
        <body style="margin:0;padding:0;background:#f3f4f6;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
          <table width="100%" cellpadding="0" cellspacing="0" role="presentation" style="background:#f3f4f6;padding:40px 16px;">
            <tr><td align="center">
              <table width="560" cellpadding="0" cellspacing="0" role="presentation" style="max-width:560px;width:100%;">
                <!-- Header -->
                <tr>
                  <td style="background:#111827;border-radius:8px 8px 0 0;padding:24px 32px;">
                    <span style="color:#ffffff;font-size:20px;font-weight:700;letter-spacing:-0.3px;">Zenride Admin</span>
                  </td>
                </tr>
                <!-- Body -->
                <tr>
                  <td style="background:#ffffff;padding:32px 32px 24px;border-radius:0 0 8px 8px;">
                    {body}
                  </td>
                </tr>
                <!-- Footer -->
                <tr>
                  <td style="padding:24px 0 0;text-align:center;color:#9ca3af;font-size:12px;line-height:1.6;">
                    &copy; {DateTime.UtcNow.Year} Zenride &bull; This is an automated message — please do not reply.<br>
                    If you have questions, contact your system administrator.
                  </td>
                </tr>
              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;

    private static string Button(string href, string label) => $"""
        <table width="100%" cellpadding="0" cellspacing="0" role="presentation" style="margin:28px 0;">
          <tr><td align="center">
            <a href="{href}" style="display:inline-block;background:#111827;color:#ffffff;text-decoration:none;font-size:15px;font-weight:600;padding:13px 30px;border-radius:6px;letter-spacing:0.1px;">{label}</a>
          </td></tr>
        </table>
        <p style="font-size:13px;color:#6b7280;text-align:center;word-break:break-all;margin:0 0 8px;">
          Or copy this link: <a href="{href}" style="color:#4b5563;">{href}</a>
        </p>
        """;

    private static string Divider() =>
        """<hr style="border:none;border-top:1px solid #e5e7eb;margin:24px 0;">""";

    private static string SmallNote(string text) =>
        $"""<p style="font-size:13px;color:#6b7280;margin:16px 0 0;line-height:1.6;">{text}</p>""";

    // ── Templates ──────────────────────────────────────────────────────────────

    public static string StaffInvite(string name, string roleKey, string inviteUrl) =>
        Wrap("You've been invited to Zenride Admin", $"""
            <p style="font-size:16px;color:#111827;font-weight:600;margin:0 0 16px;">Hi {name},</p>
            <p style="font-size:15px;color:#374151;line-height:1.6;margin:0 0 8px;">
              You have been invited to join the <strong>Zenride Admin</strong> console
              as <strong style="color:#111827;">{roleKey.Replace("_", " ")}</strong>.
            </p>
            <p style="font-size:15px;color:#374151;line-height:1.6;margin:0;">
              Click the button below to set your password and activate your account.
              This invitation expires in <strong>48 hours</strong>.
            </p>
            {Button(inviteUrl, "Accept Invitation")}
            {Divider()}
            {SmallNote("If you were not expecting this invitation, you can safely ignore this email. No account will be activated without your action.")}
            """);

    public static string PasswordResetRequested(string name, string resetUrl) =>
        Wrap("Reset your Zenride Admin password", $"""
            <p style="font-size:16px;color:#111827;font-weight:600;margin:0 0 16px;">Hi {name},</p>
            <p style="font-size:15px;color:#374151;line-height:1.6;margin:0 0 8px;">
              We received a request to reset the password for your Zenride Admin account.
            </p>
            <p style="font-size:15px;color:#374151;line-height:1.6;margin:0;">
              Click the button below to choose a new password. This link expires in <strong>30 minutes</strong>.
            </p>
            {Button(resetUrl, "Reset Password")}
            {Divider()}
            {SmallNote("If you did not request a password reset, you can safely ignore this email — your password will not change.")}
            """);

    public static string EmailVerification(string code) =>
        Wrap("Your Zenride verification code", $"""
            <p style="font-size:15px;color:#374151;line-height:1.6;margin:0 0 8px;">
              Use the code below to verify your email address.
            </p>
            <table width="100%" cellpadding="0" cellspacing="0" role="presentation" style="margin:28px 0;">
              <tr><td align="center">
                <div style="display:inline-block;background:#f3f4f6;border-radius:8px;padding:16px 40px;">
                  <span style="font-size:36px;font-weight:700;letter-spacing:8px;color:#111827;">{code}</span>
                </div>
              </td></tr>
            </table>
            {Divider()}
            {SmallNote("This code expires in 10 minutes. Do not share it with anyone. If you did not request this, you can safely ignore this email.")}
            """);

    public static string PasswordResetByAdmin(string name, string resetUrl) =>
        Wrap("Your Zenride Admin password has been reset", $"""
            <p style="font-size:16px;color:#111827;font-weight:600;margin:0 0 16px;">Hi {name},</p>
            <p style="font-size:15px;color:#374151;line-height:1.6;margin:0 0 8px;">
              An administrator has initiated a password reset for your Zenride Admin account.
            </p>
            <p style="font-size:15px;color:#374151;line-height:1.6;margin:0;">
              Click the button below to set a new password. This link expires in <strong>4 hours</strong>.
            </p>
            {Button(resetUrl, "Set New Password")}
            {Divider()}
            {SmallNote("If you did not expect this, contact your system administrator immediately.")}
            """);
}
