namespace MoneroUserForum.Data.Identity;

/// <summary>Canonical role names used across the app.</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Member = "Member";

    public static readonly string[] All = { Admin, Member };
}
