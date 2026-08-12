using MoneroUserForum.Data.Entities;

namespace MoneroUserForum.Services.Notifications;

public interface INotificationService
{
    /// <summary>
    /// Creates all notifications arising from a newly-saved post: a "reply"
    /// notice to the post it answers, "mention" notices for @handles in the body,
    /// and "thread activity" notices to everyone following the thread. Also
    /// auto-subscribes the author to the thread. Skips the author throughout and
    /// never double-notifies the same member. Saves its own changes.
    /// </summary>
    Task NotifyOnNewPostAsync(Post post, CancellationToken ct = default);

    /// <summary>Notifies the recipient of a new private message.</summary>
    Task NotifyPrivateMessageAsync(PrivateMessage message, CancellationToken ct = default);

    /// <summary>Notifies every admin that a member reported a post (into the review queue).</summary>
    Task NotifyReportAsync(Report report, CancellationToken ct = default);

    /// <summary>Notifies a post's author that <paramref name="actorUserId"/> thanked their post.
    /// In-app only (no email — thanks are frequent and low-priority). Saves its own changes.</summary>
    Task NotifyThankAsync(Post post, int actorUserId, CancellationToken ct = default);
}
