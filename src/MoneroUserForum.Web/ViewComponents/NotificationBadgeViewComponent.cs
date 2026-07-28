using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MoneroUserForum.Data;
using MoneroUserForum.Data.Identity;

namespace MoneroUserForum.Web.ViewComponents;

/// <summary>Renders the unread-notification count for the nav "Alerts" link.</summary>
public class NotificationBadgeViewComponent : ViewComponent
{
    private readonly AppDbContext db;
    private readonly UserManager<AppUser> users;

    public NotificationBadgeViewComponent(AppDbContext db, UserManager<AppUser> users)
    {
        this.db = db;
        this.users = users;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var idStr = this.users.GetUserId(this.HttpContext.User);
        if (!int.TryParse(idStr, out var uid))
        {
            return this.Content(string.Empty);
        }

        var count = await this.db.Notifications.CountAsync(n => n.RecipientUserId == uid && !n.IsRead);
        return this.View(count);
    }
}
