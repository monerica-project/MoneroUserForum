namespace MoneroUserForum.Data.Entities;

/// <summary>An image attached to a post, stored in Bunny.net.</summary>
public class PostImage
{
    public int Id { get; set; }

    public int PostId { get; set; }
    public Post? Post { get; set; }

    /// <summary>Bunny storage path/key (the CDN URL is derived from it).</summary>
    public string StoragePath { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
