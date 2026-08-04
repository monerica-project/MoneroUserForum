namespace MoneroUserForum.Services.Reputation;

/// <summary>
/// Time-gated member perks. Currently image posting unlocks one month after an
/// account is approved. Centralised here so the controller (which enforces it) and
/// the account "unlocks" page (which displays the countdown) agree exactly.
/// </summary>
public static class MemberPerks
{
    /// <summary>How long after approval image posting unlocks.</summary>
    public const int ImagePostingUnlockMonths = 1;

    /// <summary>
    /// When image posting unlocks for an account, or null if it isn't approved yet
    /// (an unapproved account has no clock running).
    /// </summary>
    public static DateTime? ImagePostingUnlockUtc(bool isApproved, DateTime? approvedAtUtc)
        => isApproved && approvedAtUtc is DateTime a ? a.AddMonths(ImagePostingUnlockMonths) : null;

    /// <summary>Whether the account may attach images to posts right now.</summary>
    public static bool CanPostImages(bool isApproved, DateTime? approvedAtUtc, DateTime nowUtc)
        => ImagePostingUnlockUtc(isApproved, approvedAtUtc) is DateTime u && u <= nowUtc;
}
