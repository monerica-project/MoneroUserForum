using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MoneroUserForum.Data;
using MoneroUserForum.Data.Enums;

namespace MoneroUserForum.Web.ViewComponents;

/// <summary>Pending-report count for the admin nav "Reports" link (admins only).</summary>
public class ReportsBadgeViewComponent : ViewComponent
{
    private readonly AppDbContext db;

    public ReportsBadgeViewComponent(AppDbContext db)
    {
        this.db = db;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (!this.HttpContext.User.IsInRole("Admin"))
        {
            return this.Content(string.Empty);
        }

        var count = await this.db.Reports.CountAsync(r => r.Status == ReportStatus.Pending);
        return this.View(count);
    }
}
