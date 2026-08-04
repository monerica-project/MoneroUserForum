using MoneroUserForum.Data.Enums;

namespace MoneroUserForum.Services.Reputation;

/// <summary>
/// Derives a member's <see cref="ReputationLevel"/> from account age and post
/// count. Three logical tiers for now (thresholds are easy to tune / extend as
/// more levels are added to the enum).
/// </summary>
public static class ReputationCalculator
{
    // Thresholds (also surfaced on the account "unlocks" page so the requirements shown match reality).
    public const int MemberMinAgeDays = 7;
    public const int MemberMinPosts = 10;
    public const int VeteranMinAgeDays = 60;
    public const int VeteranMinPosts = 100;

    public static ReputationLevel Compute(DateTime createdAtUtc, int postCount, DateTime nowUtc)
    {
        var ageDays = (nowUtc - createdAtUtc).TotalDays;

        // Veteran: established account with substantial contribution.
        if (ageDays >= VeteranMinAgeDays && postCount >= VeteranMinPosts)
        {
            return ReputationLevel.Veteran;
        }

        // Member: past the newcomer stage and actively posting.
        if (ageDays >= MemberMinAgeDays && postCount >= MemberMinPosts)
        {
            return ReputationLevel.Member;
        }

        return ReputationLevel.NewUser;
    }
}
