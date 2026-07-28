namespace MoneroUserForum.Services.Email;

public interface IEmailSender
{
    /// <summary>True when SMTP is configured; when false, reset emails can't be sent.</summary>
    bool IsConfigured { get; }

    Task SendAsync(string toAddress, string subject, string body, CancellationToken ct = default);
}
