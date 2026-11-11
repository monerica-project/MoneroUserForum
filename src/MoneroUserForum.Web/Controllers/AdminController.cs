using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MoneroUserForum.Data;
using MoneroUserForum.Data.Entities;
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
            ContactUnread = await this.db.ContactMessages.CountAsync(m => !m.IsHandled),
            ModerationFlagged = await this.db.PostModerations.CountAsync(m => m.State == PostModerationState.Flagged && !m.Post!.IsCensored),
            ModerationEnabled = MoneroUserForum.Web.Moderation.PostModerationScanner.IsConfigured(this.HttpContext.RequestServices.GetRequiredService<IConfiguration>()),
        };
        vm.Banned = (await this.RowsAsync(this.db.Users.Where(u => u.IsBanned && !u.IsDeleted)))
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(25)
            .ToList();
        return this.View(vm);
    }

    // ---- AI safety check: posts and replies that may need removing ----
    [HttpGet("moderation")]
    public async Task<IActionResult> Moderation()
    {
        static string Excerpt(string? body) =>
            body == null ? string.Empty : body.Length <= 600 ? body : body[..600] + "…";

        var rows = await this.db.PostModerations.AsNoTracking()
            .Where(m => m.State != PostModerationState.Clean && m.State != PostModerationState.Error)
            .OrderByDescending(m => m.CheckedAtUtc)
            .Select(m => new ModerationRaw
            {
                PostId = m.PostId,
                State = m.State,
                Severity = m.Severity,
                Categories = m.Categories,
                Reason = m.Reason,
                CheckedAtUtc = m.CheckedAtUtc,
                AdminActionAtUtc = m.AdminActionAtUtc,
                AdminActionBy = m.AdminActionBy,
                Body = m.Post!.Body,
                IsOpeningPost = m.Post.IsOpeningPost,
                IsCensored = m.Post.IsCensored,
                CreatedAtUtc = m.Post.CreatedAtUtc,
                AuthorId = m.Post.AuthorId,
                AuthorName = m.Post.Author!.DisplayName ?? m.Post.Author.UserName ?? "?",
                Title = m.Post.Thread!.Title,
                Slug = m.Post.Thread.Slug,
            })
            .Take(300)
            .ToListAsync();

        AdminModerationRow Row(ModerationRaw r, PostModerationState state) => new()
        {
            PostId = r.PostId, ThreadTitle = r.Title, ThreadSlug = r.Slug, IsOpeningPost = r.IsOpeningPost,
            AuthorName = r.AuthorName, AuthorId = r.AuthorId, Excerpt = Excerpt(r.Body), PostedAtUtc = r.CreatedAtUtc,
            Severity = r.Severity, Categories = r.Categories, Reason = r.Reason, State = state,
            CheckedAtUtc = r.CheckedAtUtc, AdminActionAtUtc = r.AdminActionAtUtc, AdminActionBy = r.AdminActionBy,
        };

        int Rank(string sev) => sev switch { "high" => 0, "medium" => 1, "low" => 2, _ => 3 };
        var today = DateTime.UtcNow.Date;
        var vm = new AdminModerationViewModel
        {
            // Flagged and not yet acted on (a post censored elsewhere counts as handled).
            Flagged = rows.Where(r => r.State == PostModerationState.Flagged && !r.IsCensored)
                .OrderBy(r => Rank(r.Severity)).ThenByDescending(r => r.PostId)
                .Select(r => Row(r, PostModerationState.Flagged)).ToList(),
            RecentlyHandled = rows.Where(r => r.State != PostModerationState.Flagged || r.IsCensored)
                .OrderByDescending(r => r.AdminActionAtUtc ?? r.CheckedAtUtc).Take(30)
                .Select(r => Row(r, r.IsCensored ? PostModerationState.Removed : r.State)).ToList(),
            TotalPosts = await this.db.Posts.CountAsync(p => !p.IsDeleted && !p.IsCensored),
            Checked = await this.db.PostModerations.CountAsync(m => m.State != PostModerationState.Error),
            Clean = await this.db.PostModerations.CountAsync(m => m.State == PostModerationState.Clean),
            Errors = await this.db.PostModerations.CountAsync(m => m.State == PostModerationState.Error),
            CheckedToday = await this.db.PostModerations.CountAsync(m => m.CheckedAtUtc >= today && m.Model != null),
            Enabled = MoneroUserForum.Web.Moderation.PostModerationScanner.IsConfigured(this.HttpContext.RequestServices.GetRequiredService<IConfiguration>()),
        };
        return this.View(vm);
    }

    private sealed class ModerationRaw
    {
        public int PostId { get; init; }
        public PostModerationState State { get; init; }
        public string Severity { get; init; } = "none";
        public string? Categories { get; init; }
        public string? Reason { get; init; }
        public DateTime CheckedAtUtc { get; init; }
        public DateTime? AdminActionAtUtc { get; init; }
        public string? AdminActionBy { get; init; }
        public string? Body { get; init; }
        public bool IsOpeningPost { get; init; }
        public bool IsCensored { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public int AuthorId { get; init; }
        public string AuthorName { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Slug { get; init; } = string.Empty;
    }

    /// <summary>Admin looked at a flagged post and is keeping it.</summary>
    [HttpPost("moderation/{postId:int}/keep")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ModerationKeep(int postId)
    {
        var row = await this.db.PostModerations.FirstOrDefaultAsync(m => m.PostId == postId);
        if (row == null)
        {
            return this.NotFound();
        }

        row.State = PostModerationState.Dismissed;
        row.AdminActionAtUtc = DateTime.UtcNow;
        row.AdminActionBy = this.User.Identity?.Name;
        await this.db.SaveChangesAsync();
        this.TempData["Message"] = "Kept. It won't be flagged again unless it's edited.";
        return this.RedirectToAction(nameof(this.Moderation));
    }

    // ---- Analytics: posts / replies / views over time ----

    [HttpGet("analytics")]
    public async Task<IActionResult> Analytics(DateTime? from, DateTime? to)
    {
        var today = DateTime.UtcNow.Date;
        var toDate = (to?.Date ?? today);
        var fromDate = (from?.Date ?? toDate.AddDays(-29)); // default: last 30 days (inclusive)
        if (fromDate > toDate)
        {
            (fromDate, toDate) = (toDate, fromDate);
        }

        // Clamp to a sane window so the chart and query stay bounded.
        if ((toDate - fromDate).TotalDays > 366)
        {
            fromDate = toDate.AddDays(-366);
        }

        var startUtc = DateTime.SpecifyKind(fromDate, DateTimeKind.Utc);
        var endUtc = DateTime.SpecifyKind(toDate.AddDays(1), DateTimeKind.Utc); // exclusive

        // Pull the timestamps in range and bucket by UTC day in memory — avoids any
        // provider-specific date-truncation translation and is cheap at this scale.
        var posts = await this.db.Posts
            .Where(p => p.CreatedAtUtc >= startUtc && p.CreatedAtUtc < endUtc)
            .Select(p => new { p.CreatedAtUtc, p.IsOpeningPost })
            .ToListAsync();

        var views = await this.db.ThreadViews
            .Where(v => v.CreatedAtUtc >= startUtc && v.CreatedAtUtc < endUtc)
            .Select(v => v.CreatedAtUtc)
            .ToListAsync();

        static DateOnly Bucket(DateTime utc) => DateOnly.FromDateTime(DateTime.SpecifyKind(utc, DateTimeKind.Utc).Date);

        var openByDay = posts.Where(p => p.IsOpeningPost).GroupBy(p => Bucket(p.CreatedAtUtc)).ToDictionary(g => g.Key, g => g.Count());
        var replyByDay = posts.Where(p => !p.IsOpeningPost).GroupBy(p => Bucket(p.CreatedAtUtc)).ToDictionary(g => g.Key, g => g.Count());
        var viewByDay = views.GroupBy(Bucket).ToDictionary(g => g.Key, g => g.Count());

        var points = new List<AnalyticsPoint>();
        for (var d = DateOnly.FromDateTime(fromDate); d <= DateOnly.FromDateTime(toDate); d = d.AddDays(1))
        {
            points.Add(new AnalyticsPoint
            {
                Day = d,
                Posts = openByDay.GetValueOrDefault(d),
                Replies = replyByDay.GetValueOrDefault(d),
                Views = viewByDay.GetValueOrDefault(d),
            });
        }

        return this.View(new AnalyticsViewModel
        {
            From = DateOnly.FromDateTime(fromDate),
            To = DateOnly.FromDateTime(toDate),
            Points = points,
            TotalPosts = points.Sum(p => p.Posts),
            TotalReplies = points.Sum(p => p.Replies),
            TotalViews = points.Sum(p => p.Views),
        });
    }

    // ---- Contact inbox (external feedback from the public Contact page) ----

    [HttpGet("messages")]
    public async Task<IActionResult> Messages()
    {
        var rows = await this.db.ContactMessages
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(500)
            .ToListAsync();
        return this.View(rows);
    }

    [HttpPost("messages/{id}/handled")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleContactHandled(int id)
    {
        var m = await this.db.ContactMessages.FindAsync(id);
        if (m is not null)
        {
            m.IsHandled = !m.IsHandled;
            await this.db.SaveChangesAsync();
        }

        return this.RedirectToAction(nameof(this.Messages));
    }

    [HttpPost("messages/{id}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteContact(int id)
    {
        var m = await this.db.ContactMessages.FindAsync(id);
        if (m is not null)
        {
            this.db.ContactMessages.Remove(m);
            await this.db.SaveChangesAsync();
        }

        return this.RedirectToAction(nameof(this.Messages));
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
    public async Task<IActionResult> Members(string? show = null)
    {
        // All accounts (pending, approved, banned) so an admin can set any status;
        // ?show=banned lists only banned accounts (linked from the dashboard's Banned tile).
        var bannedOnly = string.Equals(show, "banned", StringComparison.OrdinalIgnoreCase);
        var query = this.db.Users.Where(u => !u.IsDeleted && (!bannedOnly || u.IsBanned));
        this.ViewData["BannedOnly"] = bannedOnly;
        var rows = await this.RowsAsync(query);
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

    /// <summary>
    /// Step 1 of 2 (linked from the member's profile): shows what "Ban &amp; hide everything" will do.
    /// </summary>
    [HttpGet("ban-and-hide/{id:int}")]
    public async Task<IActionResult> BanAndHide(int id)
    {
        var user = await this.users.FindByIdAsync(id.ToString());
        if (user is null || user.IsDeleted)
        {
            return this.NotFound();
        }

        this.ViewData["Title"] = $"Ban {user.DisplayName}";
        return this.View(new BanAndHideViewModel
        {
            UserId = user.Id,
            DisplayName = user.DisplayName,
            IsBanned = user.IsBanned,
            IsAdmin = await this.users.IsInRoleAsync(user, Roles.Admin),
            ThreadCount = await this.db.ForumThreads.CountAsync(t => t.Posts.Any(p => p.IsOpeningPost && p.AuthorId == id) && !t.IsCensored),
            ReplyCount = await this.db.Posts.CountAsync(p => p.AuthorId == id && !p.IsOpeningPost && !p.IsCensored),
        });
    }

    /// <summary>
    /// Step 2 of 2: ban the member and censor (hide) every thread they started and every reply
    /// they wrote. Censoring is the existing reversible admin hide: censored threads drop out of
    /// every listing and 404 for non-admins; admins can still uncensor from the thread page.
    /// </summary>
    [HttpPost("ban-and-hide/{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BanAndHideConfirm(int id)
    {
        var user = await this.users.FindByIdAsync(id.ToString());
        if (user is null || user.IsDeleted)
        {
            return this.NotFound();
        }

        if (await this.users.IsInRoleAsync(user, Roles.Admin))
        {
            this.TempData["Message"] = "Admins can't be banned here.";
            return this.Redirect($"/profile/{Uri.EscapeDataString(user.DisplayName)}");
        }

        user.IsBanned = true;
        user.UpdatedAtUtc = DateTime.UtcNow;
        await this.users.UpdateAsync(user);

        var threadIds = await this.db.Posts
            .Where(p => p.IsOpeningPost && p.AuthorId == id)
            .Select(p => p.ThreadId)
            .Distinct()
            .ToListAsync();
        var threads = await this.db.ForumThreads
            .Where(t => threadIds.Contains(t.Id) && !t.IsCensored)
            .ExecuteUpdateAsync(x => x.SetProperty(t => t.IsCensored, true));
        await this.db.Posts
            .Where(p => threadIds.Contains(p.ThreadId))
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.IsCensored, true));
        var replies = await this.db.Posts
            .Where(p => p.AuthorId == id && !p.IsCensored)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.IsCensored, true));

        this.TempData["Message"] = $"Banned {user.DisplayName} and hid {threads} thread{(threads == 1 ? "" : "s")} and {replies} repl{(replies == 1 ? "y" : "ies")}.";
        return this.Redirect($"/profile/{Uri.EscapeDataString(user.DisplayName)}");
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
