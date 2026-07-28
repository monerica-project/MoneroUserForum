namespace MoneroUserForum.Web.Models.Chat;

public class ChatMessageItem
{
    public string AuthorName { get; set; } = string.Empty;
    public string? AuthorAvatarUrl { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

public class ChatViewModel
{
    public IReadOnlyList<ChatMessageItem> Messages { get; set; } = Array.Empty<ChatMessageItem>();
    public bool CanPost { get; set; }
}
