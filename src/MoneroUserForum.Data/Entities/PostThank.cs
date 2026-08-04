using MoneroUserForum.Data.Identity;

namespace MoneroUserForum.Data.Entities;

/// <summary>
/// A "thank" (like) a member gives to a post. One per (post, user) — enforced by a
/// unique index. <see cref="PostAuthorId"/> is denormalised (a post's author never
/// changes) so a member's total "thanks received" is a single GroupBy on this table
/// with no join back to Posts.
/// </summary>
public class PostThank
{
    public int Id { get; set; }

    public int PostId { get; set; }
    public Post? Post { get; set; }

    /// <summary>Member who gave the thank.</summary>
    public int UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>Author of the thanked post (denormalised for fast per-author totals).</summary>
    public int PostAuthorId { get; set; }
    public AppUser? PostAuthor { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
