namespace MoneroUserForum.Services.Moderation;

/// <summary>The check result for one post.</summary>
public sealed record ModerationVerdict(int PostId, bool Flag, string Severity, IReadOnlyList<string> Categories, string Reason);
