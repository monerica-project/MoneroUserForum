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
    /// <summary>Legacy plaintext body (old rows only; null/empty for encrypted messages).</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>The end-to-end-encrypted envelope (JSON) — decrypted in the browser. Null for legacy rows.</summary>
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

    /// <summary>Non-null when the current user may not send DMs (not approved, or too new); the reason to show.</summary>
    public string? DmBlocked { get; set; }
}

public class ComposeViewModel
{
    [Required]
    [Display(Name = "To (username)")]
    public string ToUsername { get; set; } = string.Empty;

    /// <summary>Plaintext is only ever read in the browser; it is never posted to the server.</summary>
    public string? Body { get; set; }

    /// <summary>
    /// The end-to-end-encrypted envelope produced by e2ee.js. This is what the server
    /// stores — it never sees the plaintext. Required (a message can't be sent unencrypted).
    /// </summary>
    [Required(ErrorMessage = "Your message could not be encrypted. Make sure your browser supports encryption and you're logged in.")]
    [StringLength(200000)]
    public string Envelope { get; set; } = string.Empty;
}
