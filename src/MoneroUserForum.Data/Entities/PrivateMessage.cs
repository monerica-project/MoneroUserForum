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

    public string Body { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
    public bool IsReadByRecipient { get; set; }

    public bool DeletedBySender { get; set; }
    public bool DeletedByRecipient { get; set; }
}
