namespace MoneroUserForum.Data.Entities;

/// <summary>Result of the automated (AI) safety check of one post.</summary>
public enum PostModerationState
{
    /// <summary>Checked: nothing that needs a moderator.</summary>
    Clean = 0,

    /// <summary>Potentially needs removing: shown on the admin moderation list.</summary>
    Flagged = 1,

    /// <summary>An admin looked at a flagged post and kept it.</summary>
    Dismissed = 2,

    /// <summary>An admin censored the post from the moderation list.</summary>
    Removed = 3,

    /// <summary>The check failed (API error); retried on a later run.</summary>
    Error = 4,
}

/// <summary>
/// One row per post: the latest automated safety check. Kept apart from <see cref="Post"/> so
/// the post model stays untouched. <see cref="BodyHash"/> is the hash of what was checked, so
/// an edited post is checked again.
/// </summary>
public class PostModeration
{
    public int Id { get; set; }

    public int PostId { get; set; }

    public Post? Post { get; set; }

    public string BodyHash { get; set; } = string.Empty;

    public PostModerationState State { get; set; }

    /// <summary>none, low, medium or high.</summary>
    public string Severity { get; set; } = "none";

    /// <summary>Comma-separated categories (e.g. "scam_phishing,spam").</summary>
    public string? Categories { get; set; }

    public string? Reason { get; set; }

    public string? Model { get; set; }

    public int TokensIn { get; set; }

    public int TokensOut { get; set; }

    public string? Error { get; set; }

    public DateTime CheckedAtUtc { get; set; }

    public DateTime? AdminActionAtUtc { get; set; }

    public string? AdminActionBy { get; set; }
}
