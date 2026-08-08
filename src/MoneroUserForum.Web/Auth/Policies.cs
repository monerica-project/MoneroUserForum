namespace MoneroUserForum.Web.Auth;

/// <summary>Authorization policy names used across controllers.</summary>
public static class Policies
{
    /// <summary>Signed in, approved by an admin, and not banned. Used for actions whose effect
    /// is immediately public (thanking, image posting, etc.).</summary>
    public const string CanPost = "CanPost";

    /// <summary>Signed in and not banned — approval NOT required. New members may create threads
    /// and replies right away; their content stays hidden from others until their account is
    /// approved (visibility is derived from the author's approval state at read time).</summary>
    public const string CanContribute = "CanContribute";

    /// <summary>Admin role.</summary>
    public const string AdminOnly = "AdminOnly";
}
