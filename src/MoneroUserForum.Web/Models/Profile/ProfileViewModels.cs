using System.ComponentModel.DataAnnotations;
using MoneroUserForum.Data.Enums;

namespace MoneroUserForum.Web.Models.Profile;

public class ProfileViewModel
{
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public DateTime JoinedAtUtc { get; set; }
    public int PostCount { get; set; }
    public ReputationLevel Reputation { get; set; }
    public bool IsBanned { get; set; }
    public bool IsSelf { get; set; }

    public string? WebsiteUrl { get; set; }
    public string? TwitterUrl { get; set; }
    public string? SimpleXAddress { get; set; }
    public string? TelegramHandle { get; set; }
    public string? MonericaUrl { get; set; }
    public string? PgpPublicKey { get; set; }
    public string? Signature { get; set; }
    public string? MoneroDonationAddress { get; set; }

    /// <summary>Base64 PNG data URI of the Monero tip QR (null when no donation address).</summary>
    public string? MoneroQrDataUri { get; set; }

    // Up to three free-form links (label + URL).
    public string? Link1Label { get; set; }
    public string? Link1Url { get; set; }
    public string? Link2Label { get; set; }
    public string? Link2Url { get; set; }
    public string? Link3Label { get; set; }
    public string? Link3Url { get; set; }
}

public class ProfileEditViewModel
{
    [StringLength(500)]
    [Display(Name = "Signature")]
    public string? Signature { get; set; }

    [StringLength(2048)]
    [Url]
    [Display(Name = "Website")]
    public string? WebsiteUrl { get; set; }

    [StringLength(2048)]
    [Display(Name = "X / Twitter URL")]
    public string? TwitterUrl { get; set; }

    [StringLength(512)]
    [Display(Name = "SimpleX address")]
    public string? SimpleXAddress { get; set; }

    [StringLength(64)]
    [Display(Name = "Telegram handle")]
    public string? TelegramHandle { get; set; }

    [StringLength(2048)]
    [Url]
    [Display(Name = "Your monerica.com link")]
    public string? MonericaUrl { get; set; }

    [StringLength(8000)]
    [Display(Name = "PGP public key")]
    public string? PgpPublicKey { get; set; }

    [StringLength(255)]
    [Display(Name = "Monero donation address")]
    public string? MoneroDonationAddress { get; set; }

    // Up to three free-form links (a personal site, a social profile, anything else).
    [StringLength(40)]
    [Display(Name = "Link 1 label")]
    public string? Link1Label { get; set; }

    [StringLength(2048)]
    [Url]
    [Display(Name = "Link 1 URL")]
    public string? Link1Url { get; set; }

    [StringLength(40)]
    [Display(Name = "Link 2 label")]
    public string? Link2Label { get; set; }

    [StringLength(2048)]
    [Url]
    [Display(Name = "Link 2 URL")]
    public string? Link2Url { get; set; }

    [StringLength(40)]
    [Display(Name = "Link 3 label")]
    public string? Link3Label { get; set; }

    [StringLength(2048)]
    [Url]
    [Display(Name = "Link 3 URL")]
    public string? Link3Url { get; set; }

    public string? CurrentAvatarUrl { get; set; }
}
