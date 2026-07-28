using MoneroUserForum.Data.Identity;

namespace MoneroUserForum.Data.Entities;

/// <summary>
/// A message in a thread. The thread's opening message is the post with
/// <see cref="IsOpeningPost"/> = true; every other post is a reply. A post may
/// reply to a specific earlier post (<see cref="ReplyToPostId"/>) which drives
/// the "someone replied to your post" notification and the jump-to anchor.
/// </summary>
public class Post
{
    public int Id { get; set; }

    public int ThreadId { get; set; }
    public ForumThread? Thread { get; set; }

    public int AuthorId { get; set; }
    public AppUser? Author { get; set; }

    public string Body { get; set; } = string.Empty;

    public bool IsOpeningPost { get; set; }

    public int? ReplyToPostId { get; set; }
    public Post? ReplyToPost { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Author-deleted post (kept for thread continuity, body hidden).</summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Hidden by an admin/moderator (censored) — e.g. threatening or dangerous
    /// content. The body is withheld from everyone; unlike a ban it does not
    /// depend on the author. Censoring a post also censors every reply beneath it.
    /// </summary>
    public bool IsCensored { get; set; }

    public ICollection<PostImage> Images { get; set; } = new List<PostImage>();
}
