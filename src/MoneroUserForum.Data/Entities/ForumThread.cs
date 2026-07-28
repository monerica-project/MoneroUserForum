using MoneroUserForum.Data.Enums;
using MoneroUserForum.Data.Identity;

namespace MoneroUserForum.Data.Entities;

/// <summary>
/// A discussion thread. Its opening content lives in the first <see cref="Post"/>
/// (<see cref="Post.IsOpeningPost"/> = true); subsequent posts are replies.
/// </summary>
public class ForumThread
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Lowercase URL slug, unique. Public route: /thread/{slug}.</summary>
    public string Slug { get; set; } = string.Empty;

    public ForumCategory Category { get; set; }

    public int AuthorId { get; set; }
    public AppUser? Author { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Timestamp of the most recent post — drives "recent posts" ordering.</summary>
    public DateTime LastPostAtUtc { get; set; }

    public bool IsPinned { get; set; }
    public bool IsLocked { get; set; }
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Censored by an admin — the whole thread is hidden from the public (it drops
    /// out of every listing and 404s for non-admins). Set when the opening post is
    /// censored. Reversible via uncensor.
    /// </summary>
    public bool IsCensored { get; set; }

    public int ViewCount { get; set; }

    public ICollection<Post> Posts { get; set; } = new List<Post>();
    public ICollection<ThreadSubscription> Subscriptions { get; set; } = new List<ThreadSubscription>();
    public ICollection<ThreadView> Views { get; set; } = new List<ThreadView>();
}
