using Izigo.Application.Common.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using System.Text.RegularExpressions;

namespace Izigo.Infrastructure.Services;

public class SmtpEmailService(IConfiguration config, ILogger<SmtpEmailService> logger)
    : IEmailService
{
    private string BaseUrl => config["Email:BaseUrl"]?.TrimEnd('/') ?? "";

    public string BuildLink(string path) => BaseUrl + path;

    public async Task SendAsync(string to, string toName, string subject, string htmlBody,
        CancellationToken ct = default)
    {
        var host     = config["Email:SmtpHost"];
        var port     = int.TryParse(config["Email:SmtpPort"], out var p) ? p : 587;
        var username = config["Email:Username"];
        var password = config["Email:Password"];
        var fromAddr = config["Email:FromAddress"] ?? "noreply@zenride.app";
        var fromName = config["Email:FromName"]    ?? "Zenride";

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username) || password is null)
        {
            logger.LogWarning("[Email] SMTP not configured — skipping send to {To} subject={Subject}", to, subject);
            logger.LogInformation("[Email:DEV] To={To} Subject={Subject}\n{Body}", to, subject, htmlBody);
            return;
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, fromAddr));
        message.To.Add(new MailboxAddress(toName, to));
        message.Subject = subject;

        var plain = Regex.Replace(htmlBody, "<[^>]+>", " ");
        plain = Regex.Replace(plain, @"\s{2,}", " ").Trim();

        message.Body = new BodyBuilder { HtmlBody = htmlBody, TextBody = plain }
            .ToMessageBody();

        // Port 465 → implicit SSL; everything else → STARTTLS
        var socketOptions = port == 465
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTls;

        using var client = new SmtpClient();
        try
        {
            await client.ConnectAsync(host, port, socketOptions, ct);
            await client.AuthenticateAsync(username, password, ct);
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(quit: true, ct);
            logger.LogInformation("[Email] Sent to={To} subject={Subject}", to, subject);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Email] Failed to send to={To} subject={Subject}", to, subject);
            throw;
        }
    }
}
