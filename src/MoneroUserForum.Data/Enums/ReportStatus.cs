namespace MoneroUserForum.Data.Enums;

/// <summary>Lifecycle of a member's content report in the admin review queue.</summary>
public enum ReportStatus
{
    /// <summary>Awaiting an admin's attention.</summary>
    Pending = 0,

    /// <summary>An admin acted on it (censored / deleted / banned).</summary>
    Resolved = 1,

    /// <summary>An admin reviewed it and decided no action was needed.</summary>
    Dismissed = 2,
}
