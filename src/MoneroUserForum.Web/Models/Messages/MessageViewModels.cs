using System.ComponentModel.DataAnnotations;
using MoneroUserForum.Data.Enums;

namespace MoneroUserForum.Web.Models.Messages;

public class ConversationSummary
{
    public string PartnerName { get; set; } = string.Empty;
    public string? PartnerAvatarUrl { get; set; }
    public string LastSnippet { get; set; } = string.Empty;
    public DateTime LastAtUtc { get; set; }
    public int UnreadCount { get; set; }
    public bool PartnerGone { get; set; }

    // Standing chip shown on each conversation row (mirrors the post author chip).
    public ReputationLevel PartnerReputation { get; set; }
    public bool PartnerIsAdmin { get; set; }
    public bool PartnerBanned { get; set; }
}

public class MessageItem
{
    /// <summary>The armored PGP message (or, for old rows sent before PGP was required, plain text).</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Old browser-encrypted envelope (retired system). Null for all PGP messages.</summary>
    public string? Envelope { get; set; }

    public bool FromMe { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class ConversationViewModel
{
    public string PartnerName { get; set; } = string.Empty;
    public string? PartnerAvatarUrl { get; set; }
    public bool PartnerGone { get; set; }
    public IReadOnlyList<MessageItem> Messages { get; set; } = Array.Empty<MessageItem>();

    /// <summary>Every thread the viewer is in — rendered as a navigation rail.</summary>
    public IReadOnlyList<ConversationSummary> Conversations { get; set; } = Array.Empty<ConversationSummary>();

    /// <summary>Non-null when the current user may not send DMs (not approved, too new, or no verified PGP key); the reason to show.</summary>
    public string? DmBlocked { get; set; }

    public PartnerKeyViewModel? PartnerKey { get; set; }
}

/// <summary>The other member's verified PGP key, shown so you can encrypt to it.</summary>
public class PartnerKeyViewModel
{
    public string Name { get; set; } = string.Empty;
    public bool Verified { get; set; }
    public string? PublicKey { get; set; }
    public string? Fingerprint { get; set; }
}

public class ComposeViewModel
{
    [Required]
    [Display(Name = "To (username)")]
    public string ToUsername { get; set; } = string.Empty;

    /// <summary>The armored PGP message (-----BEGIN PGP MESSAGE-----), encrypted to the recipient's verified key.
    /// Anything else is rejected — the forum only stores PGP encrypted private messages.</summary>
    [StringLength(200000)]
    [Display(Name = "PGP encrypted message")]
    public string? Body { get; set; }

    /// <summary>Recipient's key info for display (not posted).</summary>
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
    public PartnerKeyViewModel? RecipientKey { get; set; }
}
