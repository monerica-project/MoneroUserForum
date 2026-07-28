namespace MoneroUserForum.Web.Models.Search;

public class PostHit
{
    public string ThreadTitle { get; set; } = string.Empty;
    public string ThreadSlug { get; set; } = string.Empty;
    public int PostId { get; set; }
    public string Snippet { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

public class UserHit
{
    public string Name { get; set; } = string.Empty;
    public int PostCount { get; set; }
}

public class SearchViewModel
{
    public string Query { get; set; } = string.Empty;
    public IReadOnlyList<MoneroUserForum.Web.Models.Forum.ThreadListItem> Threads { get; set; } = Array.Empty<MoneroUserForum.Web.Models.Forum.ThreadListItem>();
    public IReadOnlyList<PostHit> Posts { get; set; } = Array.Empty<PostHit>();
    public IReadOnlyList<UserHit> Users { get; set; } = Array.Empty<UserHit>();
    public bool Searched { get; set; }
}
