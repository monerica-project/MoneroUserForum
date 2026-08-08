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

    public async Task<IActionResult> Index(int page = 1, string sort = "recent")
    {
        const int pageSize = 20;
        page = Math.Max(1, page);
        sort = sort == "views" || sort == "replies" ? sort : "recent";

        // Pinned threads always float to the top; the chosen metric orders the rest.
        var baseQuery = this.db.ForumThreads
            .Where(t => !t.IsDeleted && !t.IsCensored && !t.Author!.IsDeleted && t.Author!.IsApproved)
            .OrderByDescending(t => t.IsPinned);

        var sorted = sort switch
        {
            "views" => baseQuery
                .ThenByDescending(t => t.Views.Count)
                .ThenByDescending(t => t.LastPostAtUtc),
            "replies" => baseQuery
                .ThenByDescending(t => t.Posts.Count(p => !p.IsOpeningPost && !p.Author!.IsDeleted && p.Author!.IsApproved))
                .ThenByDescending(t => t.LastPostAtUtc),
            _ => baseQuery.ThenByDescending(t => t.LastPostAtUtc),
        };

        var threads = await sorted
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
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
                IsPinned = t.IsPinned,
                UniqueViews = t.Views.Count,
                PostCount = t.Posts.Count(p => p.Author!.IsApproved),
                LastPostId = t.Posts.Where(p => p.Author!.IsApproved).OrderByDescending(p => p.CreatedAtUtc).Select(p => p.Id).FirstOrDefault(),
                LastPosterName = t.Posts.Where(p => p.Author!.IsApproved).OrderByDescending(p => p.CreatedAtUtc).Select(p => p.Author!.DisplayName).FirstOrDefault(),
                LastPosterAvatarPath = t.Posts.Where(p => p.Author!.IsApproved).OrderByDescending(p => p.CreatedAtUtc).Select(p => p.Author!.AvatarPath).FirstOrDefault(),
            })
            .ToListAsync();

        this.ResolveLastPosterAvatars(threads);

        // Community teaser: total members + who joined most recently.
        var totalMembers = await this.db.Users.CountAsync(u => !u.IsDeleted && !u.IsBanned && u.IsApproved);
        var newest = await this.db.Users
            .Where(u => !u.IsDeleted && !u.IsBanned && u.IsApproved)
            .OrderByDescending(u => u.CreatedAtUtc)
            .Select(u => new { u.DisplayName, u.CreatedAtUtc })
            .FirstOrDefaultAsync();

        return this.View(new ThreadListViewModel
        {
            Threads = threads,
            Heading = sort == "views" ? "Most viewed" : sort == "replies" ? "Most replies" : "Recent discussions",
            Page = page,
            HasMore = threads.Count == pageSize,
            Sort = sort,
            TotalMembers = totalMembers,
            NewestMemberName = newest?.DisplayName,
            NewestMemberJoinedUtc = newest?.CreatedAtUtc,
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

    /// <summary>Community stats: headline totals, the newest member, and the top 25 contributors
    /// ranked by overall content contributed (threads started + replies). Deleted/banned/unapproved
    /// accounts and their hidden content are excluded, matching what's publicly visible.</summary>
    [Route("stats")]
    [Route("members")]
    public async Task<IActionResult> Members()
    {
        // Threads started, per author (visible threads + visible author).
        var threadCounts = (await this.db.ForumThreads
            .Where(t => !t.IsDeleted && !t.IsCensored && t.Author != null && !t.Author.IsDeleted && !t.Author.IsBanned && t.Author.IsApproved)
            .GroupBy(t => t.AuthorId)
            .Select(g => new { AuthorId = g.Key, Count = g.Count() })
            .ToListAsync())
            .ToDictionary(x => x.AuthorId, x => x.Count);

        // Replies (non-opening posts), per author.
        var replyCounts = (await this.db.Posts
            .Where(p => !p.IsDeleted && !p.IsOpeningPost && p.Author != null && !p.Author.IsDeleted && !p.Author.IsBanned && p.Author.IsApproved)
            .GroupBy(p => p.AuthorId)
            .Select(g => new { AuthorId = g.Key, Count = g.Count() })
            .ToListAsync())
            .ToDictionary(x => x.AuthorId, x => x.Count);

        var ranked = threadCounts.Keys.Union(replyCounts.Keys)
            .Select(id => new
            {
                Id = id,
                Threads = threadCounts.GetValueOrDefault(id),
                Replies = replyCounts.GetValueOrDefault(id),
            })
            .Where(x => x.Threads + x.Replies > 0)
            .OrderByDescending(x => x.Threads + x.Replies)
            .ThenByDescending(x => x.Threads)
            .Take(25)
            .ToList();

        var ids = ranked.Select(x => x.Id).ToList();

        var users = (await this.db.Users
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.AvatarPath, u.CreatedAtUtc })
            .ToListAsync())
            .ToDictionary(u => u.Id);

        var thanks = (await this.db.PostThanks
            .Where(t => ids.Contains(t.PostAuthorId))
            .GroupBy(t => t.PostAuthorId)
            .Select(g => new { AuthorId = g.Key, Count = g.Count() })
            .ToListAsync())
            .ToDictionary(x => x.AuthorId, x => x.Count);

        var rows = new List<MemberRankItem>();
        var rank = 0;
        foreach (var r in ranked)
        {
            if (!users.TryGetValue(r.Id, out var u))
            {
                continue;
            }

            rows.Add(new MemberRankItem
            {
                Rank = ++rank,
                DisplayName = u.DisplayName,
                ThreadCount = r.Threads,
                ReplyCount = r.Replies,
                ThanksCount = thanks.GetValueOrDefault(r.Id),
                JoinedAtUtc = u.CreatedAtUtc,
                AvatarUrl = string.IsNullOrEmpty(u.AvatarPath) ? null : this.images.PublicUrl(u.AvatarPath),
            });
        }

        var totalMembers = await this.db.Users.CountAsync(u => !u.IsDeleted && !u.IsBanned && u.IsApproved);
        var totalThreads = await this.db.ForumThreads.CountAsync(t =>
            !t.IsDeleted && !t.IsCensored && t.Author != null && !t.Author.IsDeleted && !t.Author.IsBanned && t.Author.IsApproved);
        var totalPosts = await this.db.Posts.CountAsync(p =>
            !p.IsDeleted && p.Author != null && !p.Author.IsDeleted && !p.Author.IsBanned && p.Author.IsApproved);

        var newest = await this.db.Users
            .Where(u => !u.IsDeleted && !u.IsBanned && u.IsApproved)
            .OrderByDescending(u => u.CreatedAtUtc)
            .Select(u => new { u.DisplayName, u.CreatedAtUtc, u.AvatarPath })
            .FirstOrDefaultAsync();

        return this.View(new ForumStatsViewModel
        {
            TotalMembers = totalMembers,
            TotalThreads = totalThreads,
            TotalPosts = totalPosts,
            NewestMemberName = newest?.DisplayName,
            NewestMemberJoinedUtc = newest?.CreatedAtUtc,
            NewestMemberAvatarUrl = newest != null && !string.IsNullOrEmpty(newest.AvatarPath)
                ? this.images.PublicUrl(newest.AvatarPath)
                : null,
            Top = rows,
        });
    }

    [Route("network")]
    public IActionResult Network() => this.View();

    [Route("about")]
    public IActionResult About() => this.View();
}
