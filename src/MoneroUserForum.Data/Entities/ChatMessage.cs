using MoneroUserForum.Data.Identity;

namespace MoneroUserForum.Data.Entities;

/// <summary>A message in the single global chatroom shown to all members.</summary>
public class ChatMessage
{
    public int Id { get; set; }

    public int AuthorId { get; set; }
    public AppUser? Author { get; set; }

    public string Body { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public bool IsDeleted { get; set; }
}
