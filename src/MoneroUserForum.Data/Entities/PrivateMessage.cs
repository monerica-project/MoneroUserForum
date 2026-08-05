using MoneroUserForum.Data.Identity;

namespace MoneroUserForum.Data.Entities;

/// <summary>A direct message from one member to another.</summary>
public class PrivateMessage
{
    public int Id { get; set; }

    public int SenderId { get; set; }
    public AppUser? Sender { get; set; }

    public int RecipientId { get; set; }
    public AppUser? Recipient { get; set; }

    /// <summary>
    /// Legacy plaintext body. No longer written once E2EE is on; kept for old rows only.
    /// </summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// The end-to-end-encrypted message envelope (JSON) produced in the sender's browser:
    /// a single AES-GCM ciphertext of the body plus the message key wrapped to BOTH the
    /// recipient and the sender (so each can read it), and the sender's signature. The
    /// server stores it verbatim and can never decrypt it. Null only for legacy rows.
    /// </summary>
    public string? EncryptedEnvelope { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public bool IsReadByRecipient { get; set; }

    public bool DeletedBySender { get; set; }
    public bool DeletedByRecipient { get; set; }
}
