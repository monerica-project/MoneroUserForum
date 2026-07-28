using System.ComponentModel.DataAnnotations;
using MoneroUserForum.Data.Enums;

namespace MoneroUserForum.Web.Models.Forum;

/// <summary>A thread as shown in a list (home / category).</summary>
public class ThreadListItem
{
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public ForumCategory Category { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public bool AuthorBanned { get; set; }
    public int ReplyCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime LastPostAtUtc { get; set; }
    public bool IsPinned { get; set; }
    public int UniqueViews { get; set; }

    /// <summary>Author of the most recent post (who last updated the thread).</summary>
    public string? LastPosterName { get; set; }

    /// <summary>Raw storage key of the last poster's avatar (projected from EF).</summary>
    public string? LastPosterAvatarPath { get; set; }

    /// <summary>Resolved public URL of the last poster's avatar (set after the query).</summary>
    public string? LastPosterAvatarUrl { get; set; }

    public int LastPostId { get; set; }
    public int PostCount { get; set; }
}

public class ThreadListViewModel
{
    public IReadOnlyList<ThreadListItem> Threads { get; set; } = Array.Empty<ThreadListItem>();
    public ForumCategory? Category { get; set; }
    public string? Heading { get; set; }
    public int Page { get; set; } = 1;
    public bool HasMore { get; set; }
}

/// <summary>One post inside a thread.</summary>
public class PostView
{
    public int Id { get; set; }
    public int AuthorId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string? AuthorAvatarUrl { get; set; }
    public string? AuthorSignature { get; set; }
    public ReputationLevel AuthorReputation { get; set; }
    public int AuthorPostCount { get; set; }
    public bool AuthorBanned { get; set; }
    public bool AuthorDeleted { get; set; }
    public string Body { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public bool IsCensored { get; set; }
    public bool IsOpeningPost { get; set; }
    public int? ReplyToPostId { get; set; }
    public string? ReplyToAuthorName { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public bool Edited { get; set; }

    /// <summary>Current viewer may edit/delete this post (its author, or an admin).</summary>
    public bool CanModify { get; set; }

    /// <summary>Viewer is an admin, so they can censor / uncensor this post.</summary>
    public bool CanCensor { get; set; }

    public IReadOnlyList<string> ImageUrls { get; set; } = Array.Empty<string>();
}

public class EditPostViewModel
{
    public int PostId { get; set; }
    public string ThreadSlug { get; set; } = string.Empty;

    [Required]
    [StringLength(20000, MinimumLength = 1)]
    public string Body { get; set; } = string.Empty;
}

public class ThreadDetailViewModel
{
    public int ThreadId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public ForumCategory Category { get; set; }
    public bool IsLocked { get; set; }
    public bool IsCensored { get; set; }
    public int UniqueViews { get; set; }
    public int ReplyCount { get; set; }
    public IReadOnlyList<PostView> Posts { get; set; } = Array.Empty<PostView>();
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public bool IsFollowing { get; set; }
    public ReplyViewModel Reply { get; set; } = new ();
}

public class CreateThreadViewModel
{
    [Required]
    [StringLength(200, MinimumLength = 4)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Category")]
    public ForumCategory Category { get; set; } = ForumCategory.GeneralDiscussion;

    [Required]
    [StringLength(20000, MinimumLength = 2)]
    public string Body { get; set; } = string.Empty;
}

public class ReplyViewModel
{
    public int ThreadId { get; set; }
    public int? ReplyToPostId { get; set; }

    [Required]
    [StringLength(20000, MinimumLength = 1)]
    public string Body { get; set; } = string.Empty;
}
