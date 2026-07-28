using MoneroUserForum.Data.Enums;

namespace MoneroUserForum.Web.Models.Notifications;

public class NotificationRow
{
    public int Id { get; set; }
    public NotificationType Type { get; set; }
    public string? ActorName { get; set; }
    public string? ThreadTitle { get; set; }
    public int? ThreadId { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
