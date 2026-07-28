using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MoneroUserForum.Data;
using MoneroUserForum.Services.Storage;
using MoneroUserForum.Web.Models.Forum;

namespace MoneroUserForum.Web.Controllers;

public class HomeController : Controller
{
    private readonly AppDbContext db;
    private readonly IImageStorage images;

    public HomeController(AppDbContext db, IImageStorage images)
    {
        this.db = db;
        this.images = images;
    }

    public async Task<IActionResult> Index(int page = 1)
    {
        const int pageSize = 20;
        page = Math.Max(1, page);

        var threads = await this.db.ForumThreads
            .Where(t => !t.IsDeleted && !t.IsCensored && !t.Author!.IsDeleted)
            .OrderByDescending(t => t.IsPinned)
            .ThenByDescending(t => t.LastPostAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new ThreadListItem
            {
                Title = t.Title,
                Slug = t.Slug,
                Category = t.Category,
                AuthorName = t.Author!.DisplayName,
                AuthorBanned = t.Author.IsBanned,
                ReplyCount = t.Posts.Count(p => !p.IsOpeningPost && !p.Author!.IsDeleted),
                CreatedAtUtc = t.CreatedAtUtc,
                LastPostAtUtc = t.LastPostAtUtc,
                IsPinned = t.IsPinned,
                UniqueViews = t.Views.Count,
                PostCount = t.Posts.Count,
                LastPostId = t.Posts.OrderByDescending(p => p.CreatedAtUtc).Select(p => p.Id).FirstOrDefault(),
                LastPosterName = t.Posts.OrderByDescending(p => p.CreatedAtUtc).Select(p => p.Author!.DisplayName).FirstOrDefault(),
                LastPosterAvatarPath = t.Posts.OrderByDescending(p => p.CreatedAtUtc).Select(p => p.Author!.AvatarPath).FirstOrDefault(),
            })
            .ToListAsync();

        this.ResolveLastPosterAvatars(threads);

        return this.View(new ThreadListViewModel
        {
            Threads = threads,
            Heading = "Recent discussions",
            Page = page,
            HasMore = threads.Count == pageSize,
        });
    }

    /// <summary>Turn each row's raw last-poster avatar key into a public URL.</summary>
    private void ResolveLastPosterAvatars(IEnumerable<ThreadListItem> items)
    {
        foreach (var it in items)
        {
            it.LastPosterAvatarUrl = string.IsNullOrEmpty(it.LastPosterAvatarPath)
                ? null
                : this.images.PublicUrl(it.LastPosterAvatarPath);
        }
    }

    [Route("network")]
    public IActionResult Network() => this.View();

    [Route("about")]
    public IActionResult About() => this.View();
}
