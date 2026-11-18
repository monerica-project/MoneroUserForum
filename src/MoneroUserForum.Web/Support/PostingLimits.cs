using System.Security.Claims;

namespace MoneroUserForum.Web.Support;

/// <summary>
/// Per-member hourly caps on writing. Without them one account could flood threads, bump every
/// thread, mail mentioned members, and use up the AI scanner's daily budget. Members who are not
/// approved yet (whose posts nobody else can see) get lower caps. Counts live in memory (SignupGuard),
/// so a restart resets them, which is fine for a spam brake.
/// </summary>
public static class PostingLimits
{
    public const int ThreadsPerHour = 10;
    public const int UnapprovedThreadsPerHour = 3;
    public const int RepliesPerHour = 60;
    public const int UnapprovedRepliesPerHour = 10;
    public const int EditsPerHour = 60;
    public const int UnapprovedEditsPerHour = 20;
    public const int ThanksPerHour = 120;
    public const int ChatPostsPerHour = 120;
    public const int PrivateMessagesPerHour = 30;

    public const string SlowDownMessage =
        "You're posting faster than the forum allows. Please wait a while and try again.";

    /// <summary>Counts one action for <paramref name="user"/>; false when the member's hourly cap is used up.</summary>
    public static bool TryCount(SignupGuard guard, ClaimsPrincipal user, string? userId, string action, int approvedPerHour, int unapprovedPerHour)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return false;
        }

        // Admins moderate in bulk; never slow them down.
        if (user.IsInRole("Admin"))
        {
            return true;
        }

        var cap = user.HasClaim("approved", "true") ? approvedPerHour : unapprovedPerHour;
        return guard.TryCount("post-" + action + "|" + userId, cap);
    }
}
