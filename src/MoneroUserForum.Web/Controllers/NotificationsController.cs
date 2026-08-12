using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MoneroUserForum.Data;
using MoneroUserForum.Data.Enums;
using MoneroUserForum.Data.Identity;
using MoneroUserForum.Web.Models.Notifications;

namespace MoneroUserForum.Web.Controllers;

[Authorize]
[Route("notifications")]
public class NotificationsController : Controller
{
    private const int ThreadPageSize = 20;

    private readonly AppDbContext db;
    private readonly UserManager<AppUser> users;

    public NotificationsController(AppDbContext db, UserManager<AppUser> users)
    {
        this.db = db;
        this.users = users;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var uid = this.Uid();
        var rows = await this.db.Notifications
            .Where(n => n.RecipientUserId == uid)
            .OrderByDescending(n => n.CreatedAtUtc)
            .Take(80)
            .Select(n => new NotificationRow
            {
                Id = n.Id,
                Type = n.Type,
                ActorName = n.ActorUser != null ? n.ActorUser.DisplayName : null,
                ThreadTitle = n.Thread != null ? n.Thread.Title : null,
                ThreadId = n.ThreadId,
                IsRead = n.IsRead,
                CreatedAtUtc = n.CreatedAtUtc,
            })
            .ToListAsync();

        // Opening this page IS the acknowledgement: clear the unread badge automatically.
        // rows[] already captured each item's read/unread state, so this render still
        // highlights what was new — but the bell resets and next visit shows them read.
        await this.db.Notifications
            .Where(n => n.RecipientUserId == uid && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));

        return this.View(rows);
    }

    /// <summary>Marks a notification read and jumps to the exact post (page + anchor).</summary>
    [HttpGet("open/{id:int}")]
    public async Task<IActionResult> Open(int id)
    {
        var uid = this.Uid();
        var n = await this.db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.RecipientUserId == uid);
        if (n is null)
        {
            return this.RedirectToAction(nameof(this.Index));
        }

        if (!n.IsRead)
        {
            n.IsRead = true;
            await this.db.SaveChangesAsync();
        }

        if (n.Type == NotificationType.PrivateMessage)
        {
            return this.Redirect("/messages");
        }

        if (n.PostId is int postId && n.ThreadId is int threadId)
        {
            var post = await this.db.Posts.Where(p => p.Id == postId).Select(p => new { p.CreatedAtUtc }).FirstOrDefaultAsync();
            var slug = await this.db.ForumThreads.Where(t => t.Id == threadId).Select(t => t.Slug).FirstOrDefaultAsync();
            if (post is not null && slug is not null)
            {
                var index = await this.db.Posts.CountAsync(p => p.ThreadId == threadId && p.CreatedAtUtc <= post.CreatedAtUtc);
                var page = Math.Max(1, (int)Math.Ceiling(index / (double)ThreadPageSize));
                return this.Redirect($"/thread/{slug}?page={page}#post-{postId}");
            }
        }

        return this.RedirectToAction(nameof(this.Index));
    }

    [HttpPost("read-all")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllRead()
    {
        var uid = this.Uid();
        await this.db.Notifications
            .Where(n => n.RecipientUserId == uid && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
        return this.RedirectToAction(nameof(this.Index));
    }

    /// <summary>Stop notifications for a thread the member is following.</summary>
    [HttpPost("unsubscribe/{threadId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unsubscribe(int threadId, string? returnUrl)
    {
        var uid = this.Uid();
        var sub = await this.db.ThreadSubscriptions.FirstOrDefaultAsync(s => s.ThreadId == threadId && s.UserId == uid);
        if (sub is not null && !sub.IsMuted)
        {
            sub.IsMuted = true;
            await this.db.SaveChangesAsync();
        }

        this.TempData["Message"] = "Unsubscribed from that thread.";
        return this.Redirect(returnUrl is not null && this.Url.IsLocalUrl(returnUrl) ? returnUrl : "/notifications");
    }

    private int Uid() => int.Parse(this.users.GetUserId(this.User)!);
}
