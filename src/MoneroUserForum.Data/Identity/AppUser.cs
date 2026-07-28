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

    /// <summary>Banned members stay visible but every post they made is blacked out.</summary>
    public bool IsBanned { get; set; }

    /// <summary>Self-deleted account — hides the member and all their posts.</summary>
    public bool IsDeleted { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }

    /// <summary>Note the member wrote to the admin at sign-up, shown in the approval queue.</summary>
    public string? JoinNote { get; set; }

    // ---- Profile --------------------------------------------------------

    /// <summary>Bunny storage path/key of the member's avatar (null = default logo).</summary>
    public string? AvatarPath { get; set; }

    /// <summary>Free-text signature appended under the member's posts.</summary>
    public string? Signature { get; set; }

    public string? WebsiteUrl { get; set; }
    public string? TwitterUrl { get; set; }
    public string? SimpleXAddress { get; set; }
    public string? TelegramHandle { get; set; }

    /// <summary>Member's PGP public key (shown on their profile).</summary>
    public string? PgpPublicKey { get; set; }

    /// <summary>Monero donation address shown on the profile (no faucet/tipping on-site).</summary>
    public string? MoneroDonationAddress { get; set; }

    // ---- Navigation -----------------------------------------------------

    public ICollection<ForumThread> Threads { get; set; } = new List<ForumThread>();
    public ICollection<Post> Posts { get; set; } = new List<Post>();
}
