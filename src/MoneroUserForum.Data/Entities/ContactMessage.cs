namespace MoneroUserForum.Data.Entities;

/// <summary>
/// A message sent through the public Contact page — external feedback that lands in the
/// admin inbox. Not tied to a member account; name/email are optional and self-reported.
/// </summary>
public class ContactMessage
{
    public int Id { get; set; }

    /// <summary>Optional self-reported name.</summary>
    public string? FromName { get; set; }

    /// <summary>Optional reply-to email the sender left.</summary>
    public string? FromEmail { get; set; }

    public string Message { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Admin has dealt with / read this one.</summary>
    public bool IsHandled { get; set; }
}
