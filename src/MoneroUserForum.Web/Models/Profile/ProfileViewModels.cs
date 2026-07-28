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
    public string? PgpPublicKey { get; set; }
    public string? Signature { get; set; }
    public string? MoneroDonationAddress { get; set; }
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

    [StringLength(8000)]
    [Display(Name = "PGP public key")]
    public string? PgpPublicKey { get; set; }

    [StringLength(255)]
    [Display(Name = "Monero donation address")]
    public string? MoneroDonationAddress { get; set; }

    public string? CurrentAvatarUrl { get; set; }
}
