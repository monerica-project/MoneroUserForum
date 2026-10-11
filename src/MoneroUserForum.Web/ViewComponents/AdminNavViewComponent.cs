using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MoneroUserForum.Data;

namespace MoneroUserForum.Web.ViewComponents;

/// <summary>
/// The admin nav "Admin" link (admins only). It lights up, with a count, while there are
/// members awaiting approval or unhandled contact messages.
/// </summary>
public class AdminNavViewComponent : ViewComponent
{
    private readonly AppDbContext db;

    public AdminNavViewComponent(AppDbContext db)
    {
        this.db = db;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (!this.HttpContext.User.IsInRole("Admin"))
        {
            return this.Content(string.Empty);
        }

        var pendingMembers = await this.db.Users.CountAsync(u => !u.IsApproved && !u.IsDeleted);
        var unhandledMessages = await this.db.ContactMessages.CountAsync(m => !m.IsHandled);
        return this.View((PendingMembers: pendingMembers, UnhandledMessages: unhandledMessages));
    }
}
