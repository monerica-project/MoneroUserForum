using MoneroUserForum.Data.Enums;

namespace MoneroUserForum.Services.Reputation;

/// <summary>
/// Derives a member's <see cref="ReputationLevel"/> from account age and post
/// count. Three logical tiers for now (thresholds are easy to tune / extend as
/// more levels are added to the enum).
/// </summary>
public static class ReputationCalculator
{
    public static ReputationLevel Compute(DateTime createdAtUtc, int postCount, DateTime nowUtc)
    {
        var ageDays = (nowUtc - createdAtUtc).TotalDays;

        // Veteran: established account with substantial contribution.
        if (ageDays >= 60 && postCount >= 100)
        {
            return ReputationLevel.Veteran;
        }

        // Member: past the newcomer stage and actively posting.
        if (ageDays >= 7 && postCount >= 10)
        {
            return ReputationLevel.Member;
        }

        return ReputationLevel.NewUser;
    }
}
