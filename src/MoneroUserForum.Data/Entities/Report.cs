using MoneroUserForum.Data.Enums;
using MoneroUserForum.Data.Identity;

namespace MoneroUserForum.Data.Entities;

/// <summary>
/// A member's report of a problem post (spam, abuse, threats, …). Reports land in
/// the admin review queue, where a moderator can act on the content (censor, delete,
/// ban) and then mark the report resolved, or dismiss it as not a problem.
/// </summary>
public class Report
{
    public int Id { get; set; }

    /// <summary>The thread the reported post belongs to (for linking / grouping).</summary>
    public int ThreadId { get; set; }
    public ForumThread? Thread { get; set; }

    /// <summary>The specific post being reported (the opening post = reporting the thread).</summary>
    public int PostId { get; set; }
    public Post? Post { get; set; }

    /// <summary>Member who filed the report.</summary>
    public int ReporterUserId { get; set; }
    public AppUser? Reporter { get; set; }

    /// <summary>Optional free-text reason the reporter gave.</summary>
    public string? Reason { get; set; }

    public ReportStatus Status { get; set; } = ReportStatus.Pending;

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Admin who resolved/dismissed it, and when.</summary>
    public int? HandledByUserId { get; set; }
    public DateTime? HandledAtUtc { get; set; }
}
