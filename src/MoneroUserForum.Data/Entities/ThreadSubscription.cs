using MoneroUserForum.Data.Identity;

namespace MoneroUserForum.Data.Entities;

/// <summary>
/// Tracks a member following a thread. Posting in a thread auto-subscribes the
/// member; they can unsubscribe (<see cref="IsMuted"/>) to stop notifications
/// about new activity there.
/// </summary>
public class ThreadSubscription
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    public int ThreadId { get; set; }
    public ForumThread? Thread { get; set; }

    /// <summary>Member opted out of notifications for this thread.</summary>
    public bool IsMuted { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
