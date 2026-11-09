namespace MoneroUserForum.Services.Moderation;

/// <summary>One post sent for checking.</summary>
public sealed record ModerationItem(int PostId, string? ThreadTitle, string Body);
