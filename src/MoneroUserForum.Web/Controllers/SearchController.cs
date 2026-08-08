using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MoneroUserForum.Data;
using MoneroUserForum.Services.Reputation;
using MoneroUserForum.Services.Storage;
using MoneroUserForum.Web.Models.Forum;
using MoneroUserForum.Web.Models.Search;

namespace MoneroUserForum.Web.Controllers;

[Route("search")]
public class SearchController : Controller
{
    private readonly AppDbContext db;
    private readonly IImageStorage images;

    public SearchController(AppDbContext db, IImageStorage images)
    {
        this.db = db;
        this.images = images;
    }

    /// <summary>Type-ahead thread suggestions for the nav search box.</summary>
    [HttpGet("suggest")]
    public async Task<IActionResult> Suggest(string? q)
    {
        var query = (q ?? string.Empty).Trim();
        if (query.Length < 2)
        {
            return this.Json(Array.Empty<object>());
        }

        var pattern = "%" + query.Replace("%", "\\%").Replace("_", "\\_") + "%";
        var items = await this.db.ForumThreads
            .Where(t => !t.IsDeleted && !t.IsCensored && !t.Author!.IsDeleted && t.Author!.IsApproved && EF.Functions.ILike(t.Title, pattern))
            .OrderByDescending(t => t.LastPostAtUtc)
            .Take(8)
            .Select(t => new { title = t.Title, slug = t.Slug })
            .ToListAsync();

        return this.Json(items);
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q)
    {
        var query = (q ?? string.Empty).Trim();
        var vm = new SearchViewModel { Query = query };

        if (query.Length < 2)
        {
            return this.View(vm);
        }

        vm.Searched = true;
        var pattern = "%" + query.Replace("%", "\\%").Replace("_", "\\_") + "%";

        vm.Threads = await this.db.ForumThreads
            .Where(t => !t.IsDeleted && !t.IsCensored && !t.Author!.IsDeleted && t.Author!.IsApproved && EF.Functions.ILike(t.Title, pattern))
            .OrderByDescending(t => t.LastPostAtUtc)
            .Take(25)
            .Select(t => new ThreadListItem
            {
                Title = t.Title,
                Slug = t.Slug,
                Category = t.Category,
                AuthorName = t.Author!.DisplayName,
                AuthorBanned = t.Author.IsBanned,
                ReplyCount = t.Posts.Count(p => !p.IsOpeningPost && !p.Author!.IsDeleted && p.Author!.IsApproved),
                CreatedAtUtc = t.CreatedAtUtc,
                LastPostAtUtc = t.LastPostAtUtc,
                UniqueViews = t.Views.Count,
                PostCount = t.Posts.Count(p => p.Author!.IsApproved),
                LastPostId = t.Posts.Where(p => p.Author!.IsApproved).OrderByDescending(p => p.CreatedAtUtc).Select(p => p.Id).FirstOrDefault(),
                LastPosterName = t.Posts.Where(p => p.Author!.IsApproved).OrderByDescending(p => p.CreatedAtUtc).Select(p => p.Author!.DisplayName).FirstOrDefault(),
                LastPosterAvatarPath = t.Posts.Where(p => p.Author!.IsApproved).OrderByDescending(p => p.CreatedAtUtc).Select(p => p.Author!.AvatarPath).FirstOrDefault(),
            })
            .ToListAsync();

        foreach (var it in vm.Threads)
        {
            it.LastPosterAvatarUrl = string.IsNullOrEmpty(it.LastPosterAvatarPath)
                ? null
                : this.images.PublicUrl(it.LastPosterAvatarPath);
        }

        var postRows = await this.db.Posts
            .Where(p => !p.IsDeleted && !p.IsCensored && !p.Author!.IsDeleted && !p.Author.IsBanned && p.Author.IsApproved
                        && !p.Thread!.IsDeleted && !p.Thread.IsCensored && p.Thread.Author!.IsApproved && EF.Functions.ILike(p.Body, pattern))
            .OrderByDescending(p => p.CreatedAtUtc)
            .Take(25)
            .Select(p => new
            {
                p.Id,
                ThreadTitle = p.Thread!.Title,
                ThreadSlug = p.Thread.Slug,
                p.Body,
                AuthorName = p.Author!.DisplayName,
                p.CreatedAtUtc,
            })
            .ToListAsync();

        vm.Posts = postRows.Select(p => new PostHit
        {
            PostId = p.Id,
            ThreadTitle = p.ThreadTitle,
            ThreadSlug = p.ThreadSlug,
            AuthorName = p.AuthorName,
            CreatedAtUtc = p.CreatedAtUtc,
            Snippet = p.Body.Length <= 180 ? p.Body : p.Body[..180] + "…",
        }).ToList();

        var userRows = await this.db.Users
            .Where(u => !u.IsDeleted && EF.Functions.ILike(u.DisplayName, pattern))
            .Take(20)
            .Select(u => new
            {
                u.Id,
                u.DisplayName,
                PostCount = this.db.Posts.Count(p => p.AuthorId == u.Id && !p.IsDeleted),
            })
            .ToListAsync();

        vm.Users = userRows.Select(u => new UserHit { Name = u.DisplayName, PostCount = u.PostCount }).ToList();

        return this.View(vm);
    }
}
