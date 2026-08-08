using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MoneroUserForum.Data;
using MoneroUserForum.Data.Entities;
using MoneroUserForum.Data.Enums;
using MoneroUserForum.Data.Identity;
using MoneroUserForum.Services.Notifications;
using MoneroUserForum.Services.Reputation;
using MoneroUserForum.Services.Storage;
using MoneroUserForum.Web.Auth;
using MoneroUserForum.Web.Models.Messages;

namespace MoneroUserForum.Web.Controllers;

[Authorize]
[Route("messages")]
public class MessagesController : Controller
{
    private readonly AppDbContext db;
    private readonly UserManager<AppUser> users;
    private readonly INotificationService notifications;
    private readonly IImageStorage images;

    public MessagesController(AppDbContext db, UserManager<AppUser> users, INotificationService notifications, IImageStorage images)
    {
        this.db = db;
        this.users = users;
        this.notifications = notifications;
        this.images = images;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        return this.View(await this.BuildConversationsAsync(this.Uid()));
    }

    /// <summary>
    /// Everyone the current user has a private-message thread with, newest first,
    /// with a last-message snippet and unread count. Shown as the inbox list and
    /// as the navigation rail inside a conversation so you can jump between people.
    /// </summary>
    private async Task<List<ConversationSummary>> BuildConversationsAsync(int me)
    {
        var recent = await this.db.PrivateMessages
            .Where(m => (m.SenderId == me && !m.DeletedBySender) || (m.RecipientId == me && !m.DeletedByRecipient))
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(300)
            .Select(m => new
            {
                PartnerId = m.SenderId == me ? m.RecipientId : m.SenderId,
                PartnerName = m.SenderId == me ? m.Recipient!.DisplayName : m.Sender!.DisplayName,
                PartnerAvatar = m.SenderId == me ? m.Recipient!.AvatarPath : m.Sender!.AvatarPath,
                PartnerGone = m.SenderId == me ? m.Recipient!.IsDeleted : m.Sender!.IsDeleted,
                PartnerBanned = m.SenderId == me ? m.Recipient!.IsBanned : m.Sender!.IsBanned,
                PartnerCreatedAtUtc = m.SenderId == me ? m.Recipient!.CreatedAtUtc : m.Sender!.CreatedAtUtc,
                m.Body,
                HasEnvelope = m.EncryptedEnvelope != null,
                m.CreatedAtUtc,
                Incoming = m.RecipientId == me,
                m.IsReadByRecipient,
            })
            .ToListAsync();

        var partnerIds = recent.Select(m => m.PartnerId).Distinct().ToList();

        // Batch the extras that decide each partner's standing chip: post counts
        // (feed the reputation tier) and which partners are admins.
        var postCounts = await this.db.Posts
            .Where(p => partnerIds.Contains(p.AuthorId) && !p.IsDeleted)
            .GroupBy(p => p.AuthorId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        var adminRoleId = await this.db.Roles
            .Where(r => r.Name == "Admin")
            .Select(r => r.Id)
            .FirstOrDefaultAsync();
        var adminIds = adminRoleId == 0
            ? new HashSet<int>()
            : (await this.db.UserRoles
                .Where(ur => ur.RoleId == adminRoleId && partnerIds.Contains(ur.UserId))
                .Select(ur => ur.UserId)
                .ToListAsync()).ToHashSet();

        var now = DateTime.UtcNow;

        return recent
            .GroupBy(m => m.PartnerId)
            .Select(g =>
            {
                var last = g.First();
                return new ConversationSummary
                {
                    PartnerName = last.PartnerName,
                    PartnerAvatarUrl = string.IsNullOrEmpty(last.PartnerAvatar) ? null : this.images.PublicUrl(last.PartnerAvatar),
                    PartnerGone = last.PartnerGone,
                    PartnerBanned = last.PartnerBanned,
                    PartnerIsAdmin = adminIds.Contains(g.Key),
                    PartnerReputation = ReputationCalculator.Compute(last.PartnerCreatedAtUtc, postCounts.GetValueOrDefault(g.Key), now),
                    LastSnippet = last.HasEnvelope ? "🔒 Encrypted message" : Snippet(last.Body),
                    LastAtUtc = last.CreatedAtUtc,
                    UnreadCount = g.Count(x => x.Incoming && !x.IsReadByRecipient),
                };
            })
            .OrderByDescending(c => c.LastAtUtc)
            .ToList();
    }

    [HttpGet("with/{name}")]
    public async Task<IActionResult> Conversation(string name)
    {
        var me = this.Uid();
        var partner = await this.users.FindByNameAsync(name.Trim().ToLowerInvariant());
        if (partner is null)
        {
            return this.NotFound();
        }

        var messages = await this.LoadConversationAsync(me, partner.Id);

        return this.View(new ConversationViewModel
        {
            PartnerName = partner.DisplayName,
            PartnerAvatarUrl = string.IsNullOrEmpty(partner.AvatarPath) ? null : this.images.PublicUrl(partner.AvatarPath),
            PartnerGone = partner.IsDeleted,
            Messages = messages,
            // Rail of every other thread you're in, so you can switch partners
            // without going back to the inbox. Built after LoadConversationAsync
            // so this partner's unread count is already cleared to zero.
            Conversations = await this.BuildConversationsAsync(me),
            DmBlocked = await this.DmBlockReasonAsync(),
        });
    }

    /// <summary>Just the message list — polled so incoming DMs appear without a reload.</summary>
    [HttpGet("feed/{name}")]
    public async Task<IActionResult> Feed(string name)
    {
        var me = this.Uid();
        var partner = await this.users.FindByNameAsync(name.Trim().ToLowerInvariant());
        if (partner is null)
        {
            return this.NotFound();
        }

        var messages = await this.LoadConversationAsync(me, partner.Id);
        return this.PartialView("_DmList", messages);
    }

    /// <summary>How the end-to-end encryption works + its exact guarantees and limits.</summary>
    [HttpGet("encryption")]
    [AllowAnonymous]
    public IActionResult Encryption() => this.View();

    /// <summary>Public keys for a member, so the sender's browser can encrypt to them.
    /// 404 if the member hasn't set up encryption keys yet.</summary>
    [HttpGet("pubkey/{name}")]
    public async Task<IActionResult> PubKey(string name)
    {
        var u = await this.users.FindByNameAsync(name.Trim().ToLowerInvariant());
        if (u is null || u.IsDeleted || string.IsNullOrEmpty(u.EncPublicKey) || string.IsNullOrEmpty(u.SignPublicKey))
        {
            return this.NotFound(new { error = "This member has not set up encryption yet." });
        }

        return this.Json(new { userId = u.Id, encPublicKey = u.EncPublicKey, signPublicKey = u.SignPublicKey });
    }

    private static bool IsFetch(HttpRequest req) =>
        string.Equals(req.Headers["X-Requested-With"], "fetch", StringComparison.OrdinalIgnoreCase);

    private async Task<List<MessageItem>> LoadConversationAsync(int me, int partnerId)
    {
        // Opening/reading a conversation clears its unread messages AND its
        // "sent you a message" notifications, so the alert badge goes away.
        await this.db.PrivateMessages
            .Where(m => m.RecipientId == me && m.SenderId == partnerId && !m.IsReadByRecipient)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.IsReadByRecipient, true));

        await this.db.Notifications
            .Where(n => n.RecipientUserId == me && n.ActorUserId == partnerId
                        && n.Type == NotificationType.PrivateMessage && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));

        return await this.db.PrivateMessages
            .Where(m => (m.SenderId == me && m.RecipientId == partnerId && !m.DeletedBySender)
                     || (m.SenderId == partnerId && m.RecipientId == me && !m.DeletedByRecipient))
            .OrderBy(m => m.CreatedAtUtc)
            .Select(m => new MessageItem
            {
                Body = m.Body,
                Envelope = m.EncryptedEnvelope,
                FromMe = m.SenderId == me,
                CreatedAtUtc = m.CreatedAtUtc,
            })
            .ToListAsync();
    }

    [HttpGet("new")]
    public async Task<IActionResult> New(string? to)
    {
        this.ViewData["DmBlocked"] = await this.DmBlockReasonAsync();
        return this.View("Compose", new ComposeViewModel { ToUsername = to ?? string.Empty });
    }

    /// <summary>
    /// Minimum account age before a member may send direct messages. Combined with the
    /// approval requirement below, this keeps brand-new / unvetted accounts from cold-DMing.
    /// </summary>
    private const int DmMinAccountAgeDays = 7;

    /// <summary>
    /// Returns a human-readable reason the current user may NOT send direct messages, or
    /// null when they are allowed. To send a DM an account must be <b>approved</b> AND at
    /// least <see cref="DmMinAccountAgeDays"/> days old.
    /// </summary>
    private async Task<string?> DmBlockReasonAsync()
    {
        var u = await this.users.GetUserAsync(this.User);
        if (u is null)
        {
            return "Sign in to send direct messages.";
        }

        if (!u.IsApproved)
        {
            return "Your account must be approved before you can send direct messages.";
        }

        var eligibleAt = u.CreatedAtUtc.AddDays(DmMinAccountAgeDays);
        if (DateTime.UtcNow < eligibleAt)
        {
            return $"New accounts can send direct messages once they are {DmMinAccountAgeDays} days old. " +
                   $"You'll be able to send DMs on {eligibleAt:MMMM d, yyyy} (UTC).";
        }

        return null;
    }

    /// <summary>Type-ahead member names for the "To" field on the compose form.</summary>
    [HttpGet("suggest-users")]
    public async Task<IActionResult> SuggestUsers(string? q)
    {
        var query = (q ?? string.Empty).Trim();
        if (query.Length < 1)
        {
            return this.Json(Array.Empty<object>());
        }

        var me = this.Uid();
        var pattern = "%" + query.Replace("%", "\\%").Replace("_", "\\_") + "%";
        var items = await this.db.Users
            .Where(u => !u.IsDeleted && u.Id != me && EF.Functions.ILike(u.DisplayName, pattern))
            .OrderBy(u => u.DisplayName)
            .Take(8)
            .Select(u => new { name = u.DisplayName })
            .ToListAsync();

        return this.Json(items);
    }

    [HttpPost("send")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.CanPost)]
    public async Task<IActionResult> Send(ComposeViewModel model)
    {
        if (!this.ModelState.IsValid)
        {
            return this.View("Compose", model);
        }

        // Gate: only approved accounts at least a week old may send DMs. This action also
        // handles conversation replies, so it is the single choke point to enforce it.
        var blocked = await this.DmBlockReasonAsync();
        if (blocked is not null)
        {
            this.ViewData["DmBlocked"] = blocked;
            this.ModelState.AddModelError(string.Empty, blocked);
            return this.View("Compose", model);
        }

        var me = this.Uid();
        var partner = await this.users.FindByNameAsync(model.ToUsername.Trim().ToLowerInvariant());
        if (partner is null || partner.IsDeleted)
        {
            this.ModelState.AddModelError(nameof(model.ToUsername), "No such member.");
            return this.View("Compose", model);
        }

        if (partner.Id == me)
        {
            this.ModelState.AddModelError(nameof(model.ToUsername), "You can't message yourself.");
            return this.View("Compose", model);
        }

        // Preferred path: store ONLY the browser-produced ciphertext envelope — the plaintext never
        // reaches the server. Fallback path: browsers without WebCrypto/IndexedDB (some private/Tor
        // windows, or .onion over http) can't encrypt, so they post the plaintext Body and we store
        // it unencrypted so the message can still be delivered.
        var hasEnvelope = !string.IsNullOrWhiteSpace(model.Envelope);
        var plaintext = (model.Body ?? string.Empty).Trim();

        if (!hasEnvelope && plaintext.Length == 0)
        {
            this.ModelState.AddModelError(nameof(model.Body), "Enter a message to send.");
            return this.View("Compose", model);
        }

        var pm = new PrivateMessage
        {
            SenderId = me,
            RecipientId = partner.Id,
            Body = hasEnvelope ? string.Empty : plaintext,
            EncryptedEnvelope = hasEnvelope ? model.Envelope : null,
            CreatedAtUtc = DateTime.UtcNow,
        };
        this.db.PrivateMessages.Add(pm);
        await this.db.SaveChangesAsync();

        await this.notifications.NotifyPrivateMessageAsync(pm);

        var redirect = this.Url.Action(nameof(this.Conversation), new { name = partner.DisplayName });
        if (IsFetch(this.Request))
        {
            return this.Json(new { ok = true, redirect });
        }

        return this.Redirect(redirect!);
    }

    private static string Snippet(string body)
        => body.Length <= 80 ? body : body[..80] + "…";

    private int Uid() => int.Parse(this.users.GetUserId(this.User)!);
}
