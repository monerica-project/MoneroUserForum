namespace MoneroUserForum.Services.Moderation;

/// <summary>
/// Settings for the automated post safety check ("Moderation" config section). The API key is
/// read from the environment (Anthropic__ApiKey, set by a root-only systemd EnvironmentFile),
/// never from a committed appsettings file. No key = the scanner does nothing.
/// </summary>
public sealed class ModerationOptions
{
    public bool Enabled { get; set; } = true;

    public string Model { get; set; } = "claude-opus-5-5";

    public string Effort { get; set; } = "low";

    public int MaxTokens { get; set; } = 4000;

    /// <summary>Posts per API call.</summary>
    public int BatchSize { get; set; } = 15;

    /// <summary>Most posts checked per run (runs every <see cref="IntervalMinutes"/>).</summary>
    public int PerRunCap { get; set; } = 60;

    /// <summary>Most posts checked per UTC day (bill protection).</summary>
    public int DailyCap { get; set; } = 400;

    public int IntervalMinutes { get; set; } = 10;

    /// <summary>Longest part of one post sent for checking.</summary>
    public int MaxCharsPerPost { get; set; } = 4000;

    public string BaseUrl { get; set; } = "https://api.anthropic.com/";
}
