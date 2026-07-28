using Microsoft.AspNetCore.Identity;

namespace MoneroUserForum.Data.Identity;

/// <summary>Identity role keyed by int (matches <see cref="AppUser"/>).</summary>
public class AppRole : IdentityRole<int>
{
    public AppRole()
    {
    }

    public AppRole(string name)
        : base(name)
    {
    }
}
