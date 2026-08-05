namespace MoneroUserForum.Services.Email;

/// <summary>SMTP settings for the optional-email password-reset flow.</summary>
public class EmailOptions
{
    public const string SectionName = "Email";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "Monero User Forum";
    public bool UseSsl { get; set; } = true;

    /// <summary>Absolute site base URL used to build links inside notification emails.</summary>
    public string SiteUrl { get; set; } = "https://monerouserforum.com";

    public bool Enabled => !string.IsNullOrWhiteSpace(this.Host) && !string.IsNullOrWhiteSpace(this.FromAddress);
}
