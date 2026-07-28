namespace MoneroUserForum.Web.Auth;

/// <summary>Authorization policy names used across controllers.</summary>
public static class Policies
{
    /// <summary>Signed in, approved by an admin, and not banned — required to post.</summary>
    public const string CanPost = "CanPost";

    /// <summary>Admin role.</summary>
    public const string AdminOnly = "AdminOnly";
}
