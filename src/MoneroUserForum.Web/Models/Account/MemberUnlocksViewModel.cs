using MoneroUserForum.Data.Enums;

namespace MoneroUserForum.Web.Models.Account;

/// <summary>Data for the account "what unlocks when" page.</summary>
public class MemberUnlocksViewModel
{
    public bool IsApproved { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime NowUtc { get; set; }

    public int PostCount { get; set; }
    public ReputationLevel Reputation { get; set; }

    public bool CanPostImages { get; set; }

    /// <summary>When image posting unlocks (null if unlocked already or not yet approved).</summary>
    public DateTime? ImageUnlockUtc { get; set; }

    public double AccountAgeDays => (this.NowUtc - this.CreatedAtUtc).TotalDays;
}
