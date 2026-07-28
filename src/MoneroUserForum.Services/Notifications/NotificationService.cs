using Microsoft.EntityFrameworkCore;
using MoneroUserForum.Data;
using MoneroUserForum.Data.Entities;
using MoneroUserForum.Data.Enums;
using MoneroUserForum.Services.Text;

namespace MoneroUserForum.Services.Notifications;

public class NotificationService : INotificationService
{
    private readonly AppDbContext db;

    public NotificationService(AppDbContext db)
    {
        this.db = db;
    }

    public async Task NotifyOnNewPostAsync(Post post, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var notified = new HashSet<int> { post.AuthorId }; // never notify yourself

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
            }
        }

        // 3) Thread followers (not muted, not already notified) → thread activity.
        var followerIds = await this.db.ThreadSubscriptions
            .Where(s => s.ThreadId == post.ThreadId && !s.IsMuted)
            .Select(s => s.UserId)
            .ToListAsync(ct);

        foreach (var uid in followerIds)
        {
            if (notified.Add(uid))
            {
                this.Add(NotificationType.ThreadActivity, uid, post, now);
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
        }

        await this.db.SaveChangesAsync(ct);
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
}
