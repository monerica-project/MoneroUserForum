using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MoneroUserForum.Data;
using MoneroUserForum.Data.Identity;

namespace MoneroUserForum.Web.ViewComponents;

/// <summary>Unread private-message count for the nav "Messages" link.</summary>
public class MessagesBadgeViewComponent : ViewComponent
{
    private readonly AppDbContext db;
    private readonly UserManager<AppUser> users;

    public MessagesBadgeViewComponent(AppDbContext db, UserManager<AppUser> users)
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

        var count = await this.db.PrivateMessages
            .CountAsync(m => m.RecipientId == uid && !m.IsReadByRecipient && !m.DeletedByRecipient);
        return this.View(count);
    }
}
