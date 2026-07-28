using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MoneroUserForum.Data.Identity;

namespace MoneroUserForum.Web.Auth;

/// <summary>
/// Adds forum-specific claims to the signed-in principal so authorization and
/// views can gate behaviour without a DB hit: approval state, ban state, and the
/// display name.
/// </summary>
public class AppUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<AppUser, AppRole>
{
    public AppUserClaimsPrincipalFactory(
        UserManager<AppUser> userManager,
        RoleManager<AppRole> roleManager,
        IOptions<IdentityOptions> options)
        : base(userManager, roleManager, options)
    {
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var id = await base.GenerateClaimsAsync(user);
        id.AddClaim(new Claim("approved", user.IsApproved ? "true" : "false"));
        id.AddClaim(new Claim("banned", user.IsBanned ? "true" : "false"));
        id.AddClaim(new Claim("displayname", user.DisplayName ?? user.UserName ?? string.Empty));
        return id;
    }
}
