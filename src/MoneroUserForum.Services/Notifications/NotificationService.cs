using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MoneroUserForum.Data;
using MoneroUserForum.Data.Entities;
using MoneroUserForum.Data.Enums;
using MoneroUserForum.Services.Email;
using MoneroUserForum.Services.Text;

namespace MoneroUserForum.Services.Notifications;

public class NotificationService : INotificationService
{
    private readonly AppDbContext db;
    private readonly IServiceScopeFactory scopes;
    private readonly ILogger<NotificationService> logger;

    public NotificationService(AppDbContext db, IServiceScopeFactory scopes, ILogger<NotificationService> logger)
    {
        this.db = db;
        this.scopes = scopes;
        this.logger = logger;
    }

    public async Task NotifyOnNewPostAsync(Post post, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var notified = new HashSet<int> { post.AuthorId }; // never notify yourself
        var emailTargets = new List<(int RecipientId, int ActorId, NotificationType Type)>();

        // 1) @mentions in the body → notify each mentioned member. Handled FIRST so
        //    that replying with the author's @handle reads as "mentioned you".
        var handles = MentionParser.Extract(post.Body);
        if (handles.Count > 0)
        {
            var upper = handles.Select(h => h.ToUpperInvariant()).ToList();
            var mentioned = await this.db.Users
                .Where(u => u.NormalizedUserName != null && upper.Contains(u.NormalizedUserName))
                .Select(u => u.Id)
                .ToListAsync(ct);

            foreach (var uid in mentioned)
            {
                if (notified.Add(uid))
                {
                    this.Add(NotificationType.Mention, uid, post, now);
                    emailTargets.Add((uid, post.AuthorId, NotificationType.Mention));
                }
            }
        }

        // 2) Reply to a specific post → notify that post's author (unless an
        //    @mention above already covered them).
        if (post.ReplyToPostId is int replyId)
        {
            var targetAuthorId = await this.db.Posts
                .Where(p => p.Id == replyId)
                .Select(p => (int?)p.AuthorId)
                .FirstOrDefaultAsync(ct);

            if (targetAuthorId is int rid && notified.Add(rid))
            {
                this.Add(NotificationType.Reply, rid, post, now);
                emailTargets.Add((rid, post.AuthorId, NotificationType.Reply));
            }
        }

        // 3) Everyone who has posted in this thread (participants) is notified of new
        //    replies BY DEFAULT — this is the "someone replied to my posts" alert — plus any
        //    explicit followers. A member is opted-in unless they've MUTED (unsubscribed from)
        //    the thread via the Notifications toggle on the thread page.
        var mutedIds = (await this.db.ThreadSubscriptions
            .Where(s => s.ThreadId == post.ThreadId && s.IsMuted)
            .Select(s => s.UserId)
            .ToListAsync(ct)).ToHashSet();

        var participantIds = await this.db.Posts
            .Where(p => p.ThreadId == post.ThreadId && !p.Author!.IsDeleted && p.Author!.IsApproved)
            .Select(p => p.AuthorId)
            .Distinct()
            .ToListAsync(ct);

        var followerIds = await this.db.ThreadSubscriptions
            .Where(s => s.ThreadId == post.ThreadId && !s.IsMuted)
            .Select(s => s.UserId)
            .ToListAsync(ct);

        foreach (var uid in participantIds.Concat(followerIds))
        {
            if (mutedIds.Contains(uid)) { continue; } // they unsubscribed from this thread
            if (notified.Add(uid))
            {
                this.Add(NotificationType.ThreadActivity, uid, post, now);
                emailTargets.Add((uid, post.AuthorId, NotificationType.ThreadActivity));
            }
        }

        // 4) Auto-subscribe the author to their own thread (if not already).
        var alreadySubscribed = await this.db.ThreadSubscriptions
            .AnyAsync(s => s.ThreadId == post.ThreadId && s.UserId == post.AuthorId, ct);
        if (!alreadySubscribed)
        {
            this.db.ThreadSubscriptions.Add(new ThreadSubscription
            {
                ThreadId = post.ThreadId,
                UserId = post.AuthorId,
                IsMuted = false,
                CreatedAtUtc = now,
            });
        }

        await this.db.SaveChangesAsync(ct);
        this.QueueEmails(emailTargets);
    }

    public async Task NotifyPrivateMessageAsync(PrivateMessage message, CancellationToken ct = default)
    {
        this.db.Notifications.Add(new Notification
        {
            RecipientUserId = message.RecipientId,
            ActorUserId = message.SenderId,
            Type = NotificationType.PrivateMessage,
            PrivateMessageId = message.Id,
            CreatedAtUtc = DateTime.UtcNow,
            IsRead = false,
        });

        await this.db.SaveChangesAsync(ct);
        this.QueueEmails(new List<(int, int, NotificationType)>
        {
            (message.RecipientId, message.SenderId, NotificationType.PrivateMessage),
        });
    }

    public async Task NotifyReportAsync(Report report, CancellationToken ct = default)
    {
        // Every admin gets an in-app notification (except the reporter, if they're
        // an admin). Admin membership comes from the Identity role join.
        var adminRoleId = await this.db.Roles
            .Where(r => r.Name == "Admin")
            .Select(r => r.Id)
            .FirstOrDefaultAsync(ct);
        if (adminRoleId == 0)
        {
            return;
        }

        var adminIds = await this.db.UserRoles
            .Where(ur => ur.RoleId == adminRoleId && ur.UserId != report.ReporterUserId)
            .Select(ur => ur.UserId)
            .ToListAsync(ct);
        if (adminIds.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var emailTargets = new List<(int, int, NotificationType)>();
        foreach (var adminId in adminIds)
        {
            this.db.Notifications.Add(new Notification
            {
                RecipientUserId = adminId,
                ActorUserId = report.ReporterUserId,
                Type = NotificationType.PostReported,
                ThreadId = report.ThreadId,
                PostId = report.PostId,
                CreatedAtUtc = now,
                IsRead = false,
            });
            emailTargets.Add((adminId, report.ReporterUserId, NotificationType.PostReported));
        }

        await this.db.SaveChangesAsync(ct);
        this.QueueEmails(emailTargets);
    }

    private void Add(NotificationType type, int recipientUserId, Post post, DateTime now)
    {
        this.db.Notifications.Add(new Notification
        {
            RecipientUserId = recipientUserId,
            ActorUserId = post.AuthorId,
            Type = type,
            ThreadId = post.ThreadId,
            PostId = post.Id,
            CreatedAtUtc = now,
            IsRead = false,
        });
    }

    public async Task NotifyThankAsync(Post post, int actorUserId, CancellationToken ct = default)
    {
        // Never notify yourself (you can't thank your own post anyway).
        if (post.AuthorId == actorUserId)
        {
            return;
        }

        this.db.Notifications.Add(new Notification
        {
            RecipientUserId = post.AuthorId, // the thanked author hears about it
            ActorUserId = actorUserId,       // who did the thanking
            Type = NotificationType.Thanked,
            ThreadId = post.ThreadId,
            PostId = post.Id,
            CreatedAtUtc = DateTime.UtcNow,
            IsRead = false,
        });

        await this.db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Fire-and-forget: emails each recipient who opted in AND has an email address.
    /// Runs in a fresh DI scope so it never touches the request-scoped DbContext, and
    /// never blocks the request. Message BODIES are never included (private messages are
    /// end-to-end encrypted — the server can't read them anyway) — just "log in to view".
    /// </summary>
    private void QueueEmails(List<(int RecipientId, int ActorId, NotificationType Type)> targets)
    {
        if (targets.Count == 0)
        {
            return;
        }

        var snapshot = targets.ToList();
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = this.scopes.CreateScope();
                var sp = scope.ServiceProvider;
                var sender = sp.GetRequiredService<IEmailSender>();
                if (!sender.IsConfigured)
                {
                    return;
                }

                var db2 = sp.GetRequiredService<AppDbContext>();
                var opts = sp.GetRequiredService<IOptions<EmailOptions>>().Value;

                var recipientIds = snapshot.Select(t => t.RecipientId).Distinct().ToList();
                var recipients = await db2.Users
                    .Where(u => recipientIds.Contains(u.Id) && u.EmailNotificationsEnabled
                                && u.Email != null && u.Email != string.Empty && !u.IsDeleted && !u.IsBanned)
                    .Select(u => new { u.Id, u.Email })
                    .ToDictionaryAsync(u => u.Id, u => u.Email!);
                if (recipients.Count == 0)
                {
                    return;
                }

                var actorIds = snapshot.Select(t => t.ActorId).Distinct().ToList();
                var actorNames = await db2.Users
                    .Where(u => actorIds.Contains(u.Id))
                    .Select(u => new { u.Id, u.DisplayName })
                    .ToDictionaryAsync(u => u.Id, u => u.DisplayName);

                var siteUrl = opts.SiteUrl.TrimEnd('/');
                foreach (var t in snapshot)
                {
                    if (!recipients.TryGetValue(t.RecipientId, out var toEmail))
                    {
                        continue;
                    }

                    var actor = actorNames.GetValueOrDefault(t.ActorId) ?? "Someone";
                    var verb = Verb(t.Type);
                    var link = t.Type == NotificationType.PrivateMessage ? $"{siteUrl}/messages" : siteUrl;
                    var subject = $"{actor} {verb} — Monero User Forum";
                    var body =
                        $"{actor} {verb} on the Monero User Forum.\n\n" +
                        $"Log in to view: {link}\n\n" +
                        $"— You're getting this because email notifications are on. Turn them off any time in your account settings: {siteUrl}/account/settings";
                    try
                    {
                        await sender.SendAsync(toEmail, subject, body);
                    }
                    catch (Exception ex)
                    {
                        this.logger.LogWarning(ex, "Notification email to user {Id} failed.", t.RecipientId);
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Notification email batch failed.");
            }
        });
    }

    private static string Verb(NotificationType type) => type switch
    {
        NotificationType.Reply => "replied to your post",
        NotificationType.Mention => "mentioned you",
        NotificationType.ThreadActivity => "posted in a thread you follow",
        NotificationType.PrivateMessage => "sent you a private message",
        NotificationType.PostReported => "reported a post",
        _ => "sent you a notification",
    };
}
