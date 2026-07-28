using MoneroUserForum.Data.Enums;

namespace MoneroUserForum.Web.Models.Admin;

public class AdminMemberRow
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool IsApproved { get; set; }
    public bool IsBanned { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
    public int PostCount { get; set; }
    public ReputationLevel Reputation { get; set; }
    public string? JoinNote { get; set; }
}

public class AdminDashboardViewModel
{
    public int PendingCount { get; set; }
    public int MemberCount { get; set; }
    public int BannedCount { get; set; }
    public int ThreadCount { get; set; }
    public int PostCount { get; set; }
    public int ReportsPending { get; set; }
}

public class AdminReportRow
{
    public int Id { get; set; }
    public int ThreadId { get; set; }
    public string ThreadTitle { get; set; } = string.Empty;
    public string ThreadSlug { get; set; } = string.Empty;
    public int PostId { get; set; }
    public string PostExcerpt { get; set; } = string.Empty;
    public string PostAuthor { get; set; } = string.Empty;
    public bool PostAuthorBanned { get; set; }
    public bool PostIsCensored { get; set; }
    public bool PostIsDeleted { get; set; }
    public string ReporterName { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public int ReportCountForPost { get; set; }
}
