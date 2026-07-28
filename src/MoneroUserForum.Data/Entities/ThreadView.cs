namespace MoneroUserForum.Data.Entities;

/// <summary>
/// One unique viewer of a thread. The viewer is identified by a random
/// per-browser key (a cookie value) — never an IP address — so we can show a
/// unique-view count without logging anything that identifies a person.
/// </summary>
public class ThreadView
{
    public int Id { get; set; }

    public int ThreadId { get; set; }
    public ForumThread? Thread { get; set; }

    /// <summary>Random per-browser visitor key (cookie value). Not an IP, not PII.</summary>
    public string VisitorKey { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}
