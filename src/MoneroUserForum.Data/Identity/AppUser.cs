using Microsoft.AspNetCore.Identity;
using MoneroUserForum.Data.Entities;

namespace MoneroUserForum.Data.Identity;

/// <summary>
/// A forum member. Sign-up requires no email (email is optional and, when set,
/// enables self-service password reset). Every new account starts unapproved and
/// must be approved by an admin before it can post — the anti-spam gate.
/// The Identity <see cref="IdentityUser{TKey}.UserName"/> is the public handle
/// (lowercased for uniqueness); <see cref="DisplayName"/> preserves its casing.
/// </summary>
public class AppUser : IdentityUser<int>
{
    /// <summary>Public handle as the member typed it (UserName holds the normalized form).</summary>
    public string DisplayName { get; set; } = string.Empty;

    // ---- Moderation / lifecycle ----------------------------------------

    /// <summary>Admin has approved this account; until then it cannot post.</summary>
    public bool IsApproved { get; set; }

    /// <summary>When an admin approved the account (UTC). Drives time-gated perks like
    /// image posting (unlocks 1 month after approval). Null for not-yet-approved accounts;
    /// backfilled to CreatedAtUtc for accounts approved before this field existed.</summary>
    public DateTime? ApprovedAtUtc { get; set; }

    /// <summary>Banned members stay visible but every post they made is blacked out.</summary>
    public bool IsBanned { get; set; }

    /// <summary>Self-deleted account — hides the member and all their posts.</summary>
    public bool IsDeleted { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }

    /// <summary>Note the member wrote to the admin at sign-up, shown in the approval queue.</summary>
    public string? JoinNote { get; set; }

    /// <summary>Opt-in: email me when I get a notification (reply, mention, DM, etc.).
    /// Only sends when the member also has an <see cref="IdentityUser{TKey}.Email"/> set.</summary>
    public bool EmailNotificationsEnabled { get; set; }

    // ---- Profile --------------------------------------------------------

    /// <summary>Bunny storage path/key of the member's avatar (null = default logo).</summary>
    public string? AvatarPath { get; set; }

    /// <summary>Free-text signature appended under the member's posts.</summary>
    public string? Signature { get; set; }

    public string? WebsiteUrl { get; set; }
    public string? TwitterUrl { get; set; }
    public string? SimpleXAddress { get; set; }
    public string? TelegramHandle { get; set; }

    /// <summary>The member's own listing on monerica.com (shown with a Monerica badge).</summary>
    public string? MonericaUrl { get; set; }

    /// <summary>Member's PGP public key (shown on their profile).</summary>
    public string? PgpPublicKey { get; set; }

    /// <summary>Monero donation address shown on the profile (rendered with a tip QR code).</summary>
    public string? MoneroDonationAddress { get; set; }

    // Three free-form profile links (e.g. personal site, a social profile, anything else).
    // Each is an optional label + URL; only rendered when the URL is a valid http/https link.
    public string? Link1Label { get; set; }
    public string? Link1Url { get; set; }
    public string? Link2Label { get; set; }
    public string? Link2Url { get; set; }
    public string? Link3Label { get; set; }
    public string? Link3Url { get; set; }

    // ---- End-to-end encryption (private messages) -----------------------
    // All key material is generated in the browser. The server only ever holds the
    // PUBLIC keys (a directory) plus PRIVATE keys already WRAPPED (encrypted) under a
    // key derived from the member's password — it can never unwrap them. A DB dump is
    // therefore useless for reading messages. See e2ee.js / the threat-model page.

    /// <summary>Base64 SPKI of the member's long-term ECDH P-256 public key (encryption).</summary>
    public string? EncPublicKey { get; set; }

    /// <summary>Base64 SPKI of the member's long-term ECDSA P-256 public key (signatures).</summary>
    public string? SignPublicKey { get; set; }

    /// <summary>Base64 salt for the PBKDF2 that turns the password into the key-wrapping key (KEK).</summary>
    public string? KdfSalt { get; set; }

    /// <summary>Private keys wrapped (AES-GCM) under the password-derived KEK — JSON {iv,ct}. Opaque to the server.</summary>
    public string? WrappedPrivateKeys { get; set; }

    /// <summary>Base64 salt for deriving the wrapping key from the one-time recovery code.</summary>
    public string? RecoverySalt { get; set; }

    /// <summary>Private keys wrapped under the recovery code — the "forgot password" escape hatch. Opaque to the server.</summary>
    public string? RecoveryWrappedPrivateKeys { get; set; }

    /// <summary>When the member first set up E2EE keys (null = not set up yet).</summary>
    public DateTime? E2eeEnabledUtc { get; set; }

    // ---- Navigation -----------------------------------------------------

    public ICollection<ForumThread> Threads { get; set; } = new List<ForumThread>();
    public ICollection<Post> Posts { get; set; } = new List<Post>();
}
