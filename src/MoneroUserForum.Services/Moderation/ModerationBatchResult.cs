namespace MoneroUserForum.Services.Moderation;

/// <summary>Result of one API call for a batch of posts.</summary>
public sealed record ModerationBatchResult(IReadOnlyList<ModerationVerdict> Verdicts, string Model, int TokensIn, int TokensOut, string? Error);
