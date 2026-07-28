using MoneroUserForum.Data.Enums;
using MoneroUserForum.Data.Identity;

namespace MoneroUserForum.Data.Entities;

/// <summary>
/// An in-app notification for a member (no email). Clicking it jumps to the
/// relevant post/thread (and the exact page + anchor) or private message.
/// </summary>
public class Notification
{
    public int Id { get; set; }

    /// <summary>Member who receives the notification.</summary>
    public int RecipientUserId { get; set; }
    public AppUser? RecipientUser { get; set; }

    /// <summary>Member who triggered it (null for system notifications).</summary>
    public int? ActorUserId { get; set; }
    public AppUser? ActorUser { get; set; }

    public NotificationType Type { get; set; }

    public int? ThreadId { get; set; }
    public ForumThread? Thread { get; set; }

    public int? PostId { get; set; }
    public Post? Post { get; set; }

    public int? PrivateMessageId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public bool IsRead { get; set; }
}
