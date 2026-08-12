using System.ComponentModel;

namespace MoneroUserForum.Data.Enums;

/// <summary>Why a member received an in-app notification.</summary>
public enum NotificationType
{
    Unknown = 0,

    [Description("replied to your post")]
    Reply = 1,

    [Description("mentioned you")]
    Mention = 2,

    [Description("posted in a thread you follow")]
    ThreadActivity = 3,

    [Description("sent you a message")]
    PrivateMessage = 4,

    [Description("reported a post")]
    PostReported = 5,

    [Description("thanked your post")]
    Thanked = 6,
}
