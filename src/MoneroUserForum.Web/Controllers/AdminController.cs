using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MoneroUserForum.Data;
using MoneroUserForum.Data.Enums;
using MoneroUserForum.Data.Identity;
using MoneroUserForum.Services.Reputation;
using MoneroUserForum.Web.Auth;
using MoneroUserForum.Web.Models.Admin;

namespace MoneroUserForum.Web.Controllers;

[Authorize(Policy = Policies.AdminOnly)]
[Route("admin")]
public class AdminController : Controller
{
    private readonly AppDbContext db;
    private readonly UserManager<AppUser> users;

    public AdminController(AppDbContext db, UserManager<AppUser> users)
    {
        this.db = db;
        this.users = users;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var vm = new AdminDashboardViewModel
        {
            PendingCount = await this.db.Users.CountAsync(u => !u.IsApproved && !u.IsDeleted),
            MemberCount = await this.db.Users.CountAsync(u => u.IsApproved && !u.IsDeleted),
            BannedCount = await this.db.Users.CountAsync(u => u.IsBanned && !u.IsDeleted),
            ThreadCount = await this.db.ForumThreads.CountAsync(t => !t.IsDeleted),
            PostCount = await this.db.Posts.CountAsync(),
            ReportsPending = await this.db.Reports.CountAsync(r => r.Status == ReportStatus.Pending),
        };
        return this.View(vm);
    }

    [HttpGet("reports")]
    public async Task<IActionResult> Reports()
    {
        var rows = await this.db.Reports
            .Where(r => r.Status == ReportStatus.Pending)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Select(r => new AdminReportRow
            {
                Id = r.Id,
                ThreadId = r.ThreadId,
                ThreadTitle = r.Thread!.Title,
                ThreadSlug = r.Thread.Slug,
                PostId = r.PostId,
                PostExcerpt = r.Post!.Body.Length > 200 ? r.Post.Body.Substring(0, 200) + "…" : r.Post.Body,
                PostAuthor = r.Post.Author!.DisplayName,
                PostAuthorBanned = r.Post.Author.IsBanned,
                PostIsCensored = r.Post.IsCensored,
                PostIsDeleted = r.Post.IsDeleted,
                ReporterName = r.Reporter!.DisplayName,
                Reason = r.Reason,
                CreatedAtUtc = r.CreatedAtUtc,
                ReportCountForPost = this.db.Reports.Count(x => x.PostId == r.PostId && x.Status == ReportStatus.Pending),
            })
            .Take(300)
            .ToListAsync();

        return this.View(rows);
    }

    [HttpPost("reports/{id:int}/dismiss")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> DismissReport(int id)
        => this.CloseReportAsync(id, ReportStatus.Dismissed, "Report dismissed.");

    [HttpPost("reports/{id:int}/resolve")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> ResolveReport(int id)
        => this.CloseReportAsync(id, ReportStatus.Resolved, "Report marked handled.");

    private async Task<IActionResult> CloseReportAsync(int id, ReportStatus status, string message)
    {
        var report = await this.db.Reports.FirstOrDefaultAsync(r => r.Id == id);
        if (report is not null && report.Status == ReportStatus.Pending)
        {
            report.Status = status;
            report.HandledByUserId = int.TryParse(this.users.GetUserId(this.User), out var uid) ? uid : null;
            report.HandledAtUtc = DateTime.UtcNow;
            await this.db.SaveChangesAsync();
            this.TempData["Message"] = message;
        }

        return this.RedirectToAction(nameof(this.Reports));
    }

    [HttpGet("approvals")]
    public async Task<IActionResult> Approvals()
    {
        var rows = await this.RowsAsync(this.db.Users.Where(u => !u.IsApproved && !u.IsDeleted));
        return this.View(rows);
    }

    [HttpGet("members")]
    public async Task<IActionResult> Members()
    {
        // All accounts (pending, approved, banned) so an admin can set any status.
        var rows = await this.RowsAsync(this.db.Users.Where(u => !u.IsDeleted));
        return this.View(rows);
    }

    [HttpPost("approve/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id)
    {
        var user = await this.users.FindByIdAsync(id.ToString());
        if (user is not null)
        {
            user.IsApproved = true;
            user.ApprovedAtUtc ??= DateTime.UtcNow; // starts the clock on time-gated perks; keep the original on re-approve
            user.UpdatedAtUtc = DateTime.UtcNow;
            await this.users.UpdateAsync(user); // claims refresh on their next request (no sign-out)
            this.TempData["Message"] = $"Approved {user.DisplayName}.";
        }

        return this.RedirectToAction(nameof(this.Approvals));
    }

    [HttpPost("reject/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id)
    {
        var user = await this.users.FindByIdAsync(id.ToString());
        if (user is not null && !user.IsApproved)
        {
            await this.users.DeleteAsync(user);
            this.TempData["Message"] = $"Rejected and removed {user.DisplayName}.";
        }

        return this.RedirectToAction(nameof(this.Approvals));
    }

    [HttpPost("ban/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Ban(int id)
    {
        var user = await this.users.FindByIdAsync(id.ToString());
        if (user is not null)
        {
            user.IsBanned = true;
            user.UpdatedAtUtc = DateTime.UtcNow;
            await this.users.UpdateAsync(user);
            this.TempData["Message"] = $"Banned {user.DisplayName} — their posts are now blacked out.";
        }

        return this.RedirectToAction(nameof(this.Members));
    }

    [HttpPost("unban/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unban(int id)
    {
        var user = await this.users.FindByIdAsync(id.ToString());
        if (user is not null)
        {
            user.IsBanned = false;
            user.UpdatedAtUtc = DateTime.UtcNow;
            await this.users.UpdateAsync(user);
            this.TempData["Message"] = $"Unbanned {user.DisplayName}.";
        }

        return this.RedirectToAction(nameof(this.Members));
    }

    private async Task<List<AdminMemberRow>> RowsAsync(IQueryable<AppUser> query)
    {
        var now = DateTime.UtcNow;
        var raw = await query
            .OrderByDescending(u => u.CreatedAtUtc)
            .Select(u => new
            {
                u.Id,
                u.DisplayName,
                u.IsApproved,
                u.IsBanned,
                u.CreatedAtUtc,
                u.LastLoginAtUtc,
                u.JoinNote,
                PostCount = this.db.Posts.Count(p => p.AuthorId == u.Id && !p.IsDeleted),
            })
            .Take(500)
            .ToListAsync();

        return raw.Select(u => new AdminMemberRow
        {
            Id = u.Id,
            DisplayName = u.DisplayName,
            IsApproved = u.IsApproved,
            IsBanned = u.IsBanned,
            CreatedAtUtc = u.CreatedAtUtc,
            LastLoginAtUtc = u.LastLoginAtUtc,
            PostCount = u.PostCount,
            Reputation = ReputationCalculator.Compute(u.CreatedAtUtc, u.PostCount, now),
            JoinNote = u.JoinNote,
        }).ToList();
    }
}
