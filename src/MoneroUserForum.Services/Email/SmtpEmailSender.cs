using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MoneroUserForum.Services.Email;

/// <summary>
/// Sends mail via SMTP when configured. When SMTP is not configured
/// (<see cref="IsConfigured"/> = false) calls are no-ops — the site still works
/// without email; password reset is simply unavailable to email-less accounts.
/// </summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions options;
    private readonly ILogger<SmtpEmailSender> logger;

    public SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger)
    {
        this.options = options.Value;
        this.logger = logger;
    }

    public bool IsConfigured => this.options.Enabled;

    public async Task SendAsync(string toAddress, string subject, string body, CancellationToken ct = default)
    {
        if (!this.IsConfigured)
        {
            this.logger.LogWarning("Email not configured; dropping message to {To} ({Subject}).", toAddress, subject);
            return;
        }

        using var message = new MailMessage
        {
            From = new MailAddress(this.options.FromAddress, this.options.FromName),
            Subject = subject,
            Body = body,
            IsBodyHtml = false,
        };
        message.To.Add(toAddress);

        using var client = new SmtpClient(this.options.Host, this.options.Port)
        {
            EnableSsl = this.options.UseSsl,
            Credentials = string.IsNullOrWhiteSpace(this.options.User)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(this.options.User, this.options.Password),
        };

        await client.SendMailAsync(message, ct);
    }
}
