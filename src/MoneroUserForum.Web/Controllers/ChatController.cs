using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MoneroUserForum.Data;
using MoneroUserForum.Data.Entities;
using MoneroUserForum.Data.Identity;
using MoneroUserForum.Services.Storage;
using MoneroUserForum.Web.Auth;
using MoneroUserForum.Web.Hubs;
using MoneroUserForum.Web.Models.Chat;

namespace MoneroUserForum.Web.Controllers;

[Authorize]
[Route("chat")]
public class ChatController : Controller
{
    private const int Recent = 60;

    private readonly AppDbContext db;
    private readonly UserManager<AppUser> users;
    private readonly IImageStorage images;
    private readonly IHubContext<ChatHub> chatHub;

    public ChatController(AppDbContext db, UserManager<AppUser> users, IImageStorage images, IHubContext<ChatHub> chatHub)
    {
        this.db = db;
        this.users = users;
        this.images = images;
        this.chatHub = chatHub;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        return this.View(new ChatViewModel
        {
            Messages = await this.LoadAsync(),
            CanPost = this.User.HasClaim("approved", "true") && !this.User.HasClaim("banned", "true"),
        });
    }

    /// <summary>Just the message log — polled by chat.js to keep the room fresh.</summary>
    [HttpGet("feed")]
    public async Task<IActionResult> Feed()
        => this.PartialView("_ChatLog", await this.LoadAsync());

    [HttpPost("post")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.CanPost)]
    public async Task<IActionResult> Post(string body)
    {
        body = (body ?? string.Empty).Trim();
        if (body.Length is > 0 and <= 2000)
        {
            this.db.ChatMessages.Add(new ChatMessage
            {
                AuthorId = int.Parse(this.users.GetUserId(this.User)!),
                Body = body,
                CreatedAtUtc = DateTime.UtcNow,
            });
            await this.db.SaveChangesAsync();

            // Nudge every connected client to pull the fresh feed (real-time).
            await this.chatHub.Clients.All.SendAsync("chatUpdated");
        }

        // AJAX (chat.js) doesn't need a body back — it re-polls the feed. Plain
        // form posts (no JS) get the normal redirect.
        if (this.Request.Headers.XRequestedWith == "fetch")
        {
            return this.NoContent();
        }

        return this.RedirectToAction(nameof(this.Index));
    }

    private async Task<List<ChatMessageItem>> LoadAsync()
    {
        var rows = await this.db.ChatMessages
            .Where(m => !m.IsDeleted && !m.Author!.IsBanned && !m.Author.IsDeleted)
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(Recent)
            .Select(m => new
            {
                m.Author!.DisplayName,
                m.Author.AvatarPath,
                m.Body,
                m.CreatedAtUtc,
            })
            .ToListAsync();

        rows.Reverse(); // oldest first for display
        return rows.Select(r => new ChatMessageItem
        {
            AuthorName = r.DisplayName,
            AuthorAvatarUrl = string.IsNullOrEmpty(r.AvatarPath) ? null : this.images.PublicUrl(r.AvatarPath),
            Body = r.Body,
            CreatedAtUtc = r.CreatedAtUtc,
        }).ToList();
    }
}
