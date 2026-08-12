using System.ComponentModel.DataAnnotations;
using MoneroUserForum.Data.Enums;

namespace MoneroUserForum.Web.Models.Profile;

/// <summary>One row in the "posts &amp; comments" list on a member's profile.</summary>
public class ProfilePostItem
{
    public int PostId { get; set; }
    public string ThreadSlug { get; set; } = string.Empty;
    public string ThreadTitle { get; set; } = string.Empty;
    public bool IsOpeningPost { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string Snippet { get; set; } = string.Empty;
}

/// <summary>One row on the "thanks given / received" list — a single thank event on a post.</summary>
public class ThanksListItem
{
    public int PostId { get; set; }
    public string ThreadSlug { get; set; } = string.Empty;
    public string ThreadTitle { get; set; } = string.Empty;
    public bool IsOpeningPost { get; set; }

    /// <summary>1-based thread page the post falls on, so its link lands on the post.</summary>
    public int ThreadPage { get; set; } = 1;

    /// <summary>The other member: who gave the thank (received view) or whose post was thanked (given view).</summary>
    public string OtherName { get; set; } = string.Empty;
    public DateTime WhenUtc { get; set; }
}

public class ThanksListViewModel
{
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>true = thanks this member received; false = thanks this member gave out.</summary>
    public bool IsReceived { get; set; }
    public int Total { get; set; }
    public IReadOnlyList<ThanksListItem> Items { get; set; } = Array.Empty<ThanksListItem>();
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
}

public class ProfileViewModel
{
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public DateTime JoinedAtUtc { get; set; }
    public int PostCount { get; set; }
    public int ThanksReceived { get; set; }
    public int ThanksGiven { get; set; }
    public ReputationLevel Reputation { get; set; }

    // Paged list of this member's threads and replies.
    public IReadOnlyList<ProfilePostItem> RecentPosts { get; set; } = Array.Empty<ProfilePostItem>();
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
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
