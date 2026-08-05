using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MoneroUserForum.Data;
using MoneroUserForum.Data.Entities;
using MoneroUserForum.Data.Enums;
using MoneroUserForum.Data.Identity;
using MoneroUserForum.Services.Notifications;
using MoneroUserForum.Services.Reputation;
using MoneroUserForum.Services.Storage;
using MoneroUserForum.Services.Text;
using MoneroUserForum.Web.Auth;
using MoneroUserForum.Web.Models.Forum;
using MoneroUserForum.Web.Support;

namespace MoneroUserForum.Web.Controllers;

[Route("thread")]
public class ThreadController : Controller
{
    private const int PageSize = 20;

    // Post image attachments (unlocked 1 month after approval — see MemberPerks).
    private const int MaxPostImages = 4;
    private const long MaxPostImageBytes = 5 * 1024 * 1024;
    private static readonly HashSet<string> AllowedImageExt =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

    private readonly AppDbContext db;
    private readonly UserManager<AppUser> users;
    private readonly INotificationService notifications;
    private readonly IImageStorage images;

    public ThreadController(AppDbContext db, UserManager<AppUser> users, INotificationService notifications, IImageStorage images)
    {
        this.db = db;
        this.users = users;
        this.notifications = notifications;
        this.images = images;
    }

    /// <summary>Only threads that should be publicly visible (not deleted, not censored, author not self-deleted).</summary>
    private IQueryable<ForumThread> VisibleThreads()
        => this.db.ForumThreads.Where(t => !t.IsDeleted && !t.IsCensored && !t.Author!.IsDeleted);

    public async Task<List<ThreadListItem>> BuildListAsync(IQueryable<ForumThread> query, int page)
    {
        var items = await query
            .OrderByDescending(t => t.IsPinned)
            .ThenByDescending(t => t.LastPostAtUtc)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
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

        foreach (var it in items)
        {
            it.LastPosterAvatarUrl = string.IsNullOrEmpty(it.LastPosterAvatarPath)
                ? null
                : this.images.PublicUrl(it.LastPosterAvatarPath);
        }

        return items;
    }

    [HttpGet("categories")]
    public IActionResult Categories()
    {
        var cats = Enum.GetValues<ForumCategory>().Where(c => c != ForumCategory.Unknown).ToList();
        return this.View(cats);
    }

    [HttpGet("category/{category}")]
    public async Task<IActionResult> Category(ForumCategory category, int page = 1)
    {
        if (category == ForumCategory.Unknown)
        {
            return this.NotFound();
        }

        page = Math.Max(1, page);
        var items = await this.BuildListAsync(this.VisibleThreads().Where(t => t.Category == category), page);
        return this.View(new ThreadListViewModel
        {
            Threads = items,
            Category = category,
            Heading = Support.ForumDisplay.Describe(category),
            Page = page,
            HasMore = items.Count == PageSize,
        });
    }

    [HttpGet("{slug}")]
    public async Task<IActionResult> View(string slug, int page = 1)
    {
        var isAdmin = this.User.IsInRole("Admin");

        // Censored threads stay reachable by admins (to review / uncensor) but 404
        // for everyone else, just like a deleted thread.
        var thread = await this.db.ForumThreads
            .Where(t => !t.IsDeleted && !t.Author!.IsDeleted)
            .FirstOrDefaultAsync(t => t.Slug == slug);
        if (thread is null || (thread.IsCensored && !isAdmin))
        {
            return this.NotFound();
        }

        // Unique views — identified by a random per-browser cookie key, never an IP.
        var visitorKey = this.VisitorKey();
        if (!await this.db.ThreadViews.AnyAsync(v => v.ThreadId == thread.Id && v.VisitorKey == visitorKey))
        {
            this.db.ThreadViews.Add(new ThreadView { ThreadId = thread.Id, VisitorKey = visitorKey, CreatedAtUtc = DateTime.UtcNow });
            try
            {
                await this.db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Two tabs at once raced the unique index — the view is already counted.
                this.db.ChangeTracker.Clear();
            }
        }

        var uniqueViews = await this.db.ThreadViews.CountAsync(v => v.ThreadId == thread.Id);

        var totalPosts = await this.db.Posts.CountAsync(p => p.ThreadId == thread.Id);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalPosts / (double)PageSize));
        page = Math.Clamp(page, 1, totalPages);

        var raw = await this.db.Posts
            .Where(p => p.ThreadId == thread.Id)
            .OrderBy(p => p.CreatedAtUtc)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(p => new
            {
                p.Id,
                p.AuthorId,
                AuthorName = p.Author!.DisplayName,
                p.Author.AvatarPath,
                p.Author.Signature,
                AuthorCreatedAtUtc = p.Author.CreatedAtUtc,
                AuthorBanned = p.Author.IsBanned,
                AuthorDeleted = p.Author.IsDeleted,
                p.Body,
                p.IsDeleted,
                p.IsCensored,
                p.IsOpeningPost,
                p.ReplyToPostId,
                ReplyToAuthorName = p.ReplyToPost != null ? p.ReplyToPost.Author!.DisplayName : null,
                p.CreatedAtUtc,
                p.UpdatedAtUtc,
                Images = p.Images.OrderBy(i => i.SortOrder).Select(i => i.StoragePath).ToList(),
            })
            .ToListAsync();

        // Reputation: batch author post counts for this page.
        var authorIds = raw.Select(r => r.AuthorId).Distinct().ToList();
        var postCounts = await this.db.Posts
            .Where(p => authorIds.Contains(p.AuthorId) && !p.IsDeleted)
            .GroupBy(p => p.AuthorId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        var now = DateTime.UtcNow;
        var meId = this.CurrentUserId();

        // Thanks: batch per-post counts (for the button) and per-author totals
        // (shown under the avatar) for this page in two grouped queries.
        var pagePostIds = raw.Select(r => r.Id).ToList();
        var thankCounts = await this.db.PostThanks
            .Where(t => pagePostIds.Contains(t.PostId))
            .GroupBy(t => t.PostId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        var thanksReceived = await this.db.PostThanks
            .Where(t => authorIds.Contains(t.PostAuthorId))
            .GroupBy(t => t.PostAuthorId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        // Which of this page's posts the current viewer has already thanked.
        var myThanks = meId is int myId
            ? (await this.db.PostThanks
                .Where(t => t.UserId == myId && pagePostIds.Contains(t.PostId))
                .Select(t => t.PostId)
                .ToListAsync()).ToHashSet()
            : new HashSet<int>();

        // Viewer may thank if they're an approved, non-banned member.
        var viewerCanThank = this.User.HasClaim("approved", "true") && !this.User.HasClaim("banned", "true");
        var posts = raw.Select(r => new PostView
        {
            Id = r.Id,
            AuthorId = r.AuthorId,
            AuthorName = r.AuthorName,
            AuthorAvatarUrl = string.IsNullOrEmpty(r.AvatarPath) ? null : this.images.PublicUrl(r.AvatarPath),
            AuthorSignature = r.Signature,
            AuthorReputation = ReputationCalculator.Compute(r.AuthorCreatedAtUtc, postCounts.GetValueOrDefault(r.AuthorId), now),
            AuthorPostCount = postCounts.GetValueOrDefault(r.AuthorId),
            AuthorThanksReceived = thanksReceived.GetValueOrDefault(r.AuthorId),
            ThankCount = thankCounts.GetValueOrDefault(r.Id),
            ViewerThanked = myThanks.Contains(r.Id),
            CanThank = viewerCanThank && meId != r.AuthorId && !r.IsDeleted && !r.IsCensored && !r.AuthorBanned && !r.AuthorDeleted,
            AuthorBanned = r.AuthorBanned,
            AuthorDeleted = r.AuthorDeleted,
            Body = r.Body,
            IsDeleted = r.IsDeleted,
            IsCensored = r.IsCensored,
            IsOpeningPost = r.IsOpeningPost,
            ReplyToPostId = r.ReplyToPostId,
            ReplyToAuthorName = r.ReplyToAuthorName,
            CreatedAtUtc = r.CreatedAtUtc,
            Edited = r.UpdatedAtUtc > r.CreatedAtUtc.AddSeconds(5),
            CanModify = !r.AuthorDeleted && ((meId == r.AuthorId) || isAdmin),
            CanCensor = isAdmin,
            ImageUrls = r.Images.Select(i => this.images.PublicUrl(i)).ToList(),
        }).ToList();

        var following = meId is int uid && await this.db.ThreadSubscriptions
            .AnyAsync(s => s.ThreadId == thread.Id && s.UserId == uid && !s.IsMuted);

        // Image-posting perk (approved ≥1 month) — drives the reply form's picker + hint.
        var canPostImages = false;
        DateTime? imageUnlockUtc = null;
        if (meId is int meUid)
        {
            var me = await this.db.Users.AsNoTracking()
                .Select(x => new { x.Id, x.IsApproved, x.ApprovedAtUtc })
                .FirstOrDefaultAsync(x => x.Id == meUid);
            if (me is not null)
            {
                canPostImages = MemberPerks.CanPostImages(me.IsApproved, me.ApprovedAtUtc, now);
                imageUnlockUtc = MemberPerks.ImagePostingUnlockUtc(me.IsApproved, me.ApprovedAtUtc);
            }
        }

        return this.View(new ThreadDetailViewModel
        {
            ThreadId = thread.Id,
            Title = thread.Title,
            Slug = thread.Slug,
            Category = thread.Category,
            IsLocked = thread.IsLocked,
            IsCensored = thread.IsCensored,
            UniqueViews = uniqueViews,
            ReplyCount = Math.Max(0, totalPosts - 1),
            Posts = posts,
            Page = page,
            TotalPages = totalPages,
            IsFollowing = following,
            Reply = new ReplyViewModel { ThreadId = thread.Id },
            CanPostImages = canPostImages,
            ImageUnlockUtc = imageUnlockUtc,
        });
    }

    private string VisitorKey()
    {
        const string name = "muf.vid";
        var vid = this.Request.Cookies[name];
        if (string.IsNullOrEmpty(vid) || vid.Length > 64)
        {
            vid = Guid.NewGuid().ToString("N");
            this.Response.Cookies.Append(name, vid, new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = this.Request.IsHttps,
                Expires = DateTimeOffset.UtcNow.AddYears(2),
            });
        }

        return vid;
    }

    [HttpGet("create")]
    [Authorize(Policy = Policies.CanPost)]
    public async Task<IActionResult> Create(ForumCategory category = ForumCategory.GeneralDiscussion)
    {
        await this.SetImagePerkViewBagAsync();
        return this.View(new CreateThreadViewModel { Category = category == ForumCategory.Unknown ? ForumCategory.GeneralDiscussion : category });
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.CanPost)]
    public async Task<IActionResult> Create(CreateThreadViewModel model, List<IFormFile>? images)
    {
        await this.SetImagePerkViewBagAsync();
        if (model.Category == ForumCategory.Unknown)
        {
            this.ModelState.AddModelError(nameof(model.Category), "Choose a category.");
        }

        if (!this.ModelState.IsValid)
        {
            return this.View(model);
        }

        var authorId = this.CurrentUserId()!.Value;
        var now = DateTime.UtcNow;

        var slug = SlugHelper.Slugify(model.Title);
        if (string.IsNullOrEmpty(slug))
        {
            slug = "thread";
        }

        // A live thread with this title already exists — point the member to it
        // instead of creating a near-duplicate.
        var clash = await this.db.ForumThreads.FirstOrDefaultAsync(t => t.Slug == slug);
        if (clash is { IsDeleted: false })
        {
            this.ModelState.AddModelError(nameof(model.Title), "A thread with this title already exists.");
            this.ViewBag.ExistingSlug = clash.Slug;
            this.ViewBag.ExistingTitle = clash.Title;
            return this.View(model);
        }

        // Only a deleted thread holds the slug — nudge past it so the unique index is happy.
        var baseSlug = slug;
        var n = 1;
        while (await this.db.ForumThreads.AnyAsync(t => t.Slug == slug))
        {
            slug = $"{baseSlug}-{++n}";
        }

        var thread = new ForumThread
        {
            Title = model.Title.Trim(),
            Slug = slug,
            Category = model.Category,
            AuthorId = authorId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            LastPostAtUtc = now,
        };
        this.db.ForumThreads.Add(thread);
        await this.db.SaveChangesAsync();

        var opening = new Post
        {
            ThreadId = thread.Id,
            AuthorId = authorId,
            Body = model.Body.Trim(),
            IsOpeningPost = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        this.db.Posts.Add(opening);
        await this.db.SaveChangesAsync();

        await this.AttachImagesAsync(opening, images, now);

        await this.notifications.NotifyOnNewPostAsync(opening);

        return this.RedirectToAction(nameof(this.View), new { slug = thread.Slug });
    }

    [HttpPost("reply")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.CanPost)]
    public async Task<IActionResult> Reply(ReplyViewModel model, List<IFormFile>? images)
    {
        var thread = await this.db.ForumThreads.FirstOrDefaultAsync(t => t.Id == model.ThreadId && !t.IsDeleted);
        if (thread is null)
        {
            return this.NotFound();
        }

        if (thread.IsLocked)
        {
            this.TempData["Error"] = "This thread is locked.";
            return this.RedirectToAction(nameof(this.View), new { slug = thread.Slug });
        }

        if (!this.ModelState.IsValid)
        {
            this.TempData["Error"] = "Your reply was empty.";
            return this.RedirectToAction(nameof(this.View), new { slug = thread.Slug });
        }

        var authorId = this.CurrentUserId()!.Value;
        var now = DateTime.UtcNow;

        var post = new Post
        {
            ThreadId = thread.Id,
            AuthorId = authorId,
            Body = model.Body.Trim(),
            ReplyToPostId = model.ReplyToPostId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        this.db.Posts.Add(post);
        thread.LastPostAtUtc = now;
        thread.UpdatedAtUtc = now;
        await this.db.SaveChangesAsync();

        await this.AttachImagesAsync(post, images, now);

        await this.notifications.NotifyOnNewPostAsync(post);

        var lastPage = Math.Max(1, (int)Math.Ceiling(
            await this.db.Posts.CountAsync(p => p.ThreadId == thread.Id) / (double)PageSize));
        return this.Redirect($"/thread/{thread.Slug}?page={lastPage}#post-{post.Id}");
    }

    [HttpGet("post/{id:int}/edit")]
    [Authorize(Policy = Policies.CanPost)]
    public async Task<IActionResult> EditPost(int id)
    {
        var post = await this.db.Posts.Include(p => p.Thread).FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);
        if (post?.Thread is null)
        {
            return this.NotFound();
        }

        if (!this.CanModify(post.AuthorId))
        {
            return this.Forbid();
        }

        return this.View(new EditPostViewModel { PostId = post.Id, ThreadSlug = post.Thread.Slug, Body = post.Body });
    }

    [HttpPost("post/{id:int}/edit")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.CanPost)]
    public async Task<IActionResult> EditPost(int id, EditPostViewModel model)
    {
        var post = await this.db.Posts.Include(p => p.Thread).FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);
        if (post?.Thread is null)
        {
            return this.NotFound();
        }

        if (!this.CanModify(post.AuthorId))
        {
            return this.Forbid();
        }

        if (!this.ModelState.IsValid)
        {
            model.PostId = post.Id;
            model.ThreadSlug = post.Thread.Slug;
            return this.View(model);
        }

        post.Body = model.Body.Trim();
        post.UpdatedAtUtc = DateTime.UtcNow;
        await this.db.SaveChangesAsync();
        return this.Redirect($"/thread/{post.Thread.Slug}#post-{post.Id}");
    }

    [HttpPost("post/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.CanPost)]
    public async Task<IActionResult> DeletePost(int id)
    {
        var post = await this.db.Posts.Include(p => p.Thread).FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted);
        if (post?.Thread is null)
        {
            return this.NotFound();
        }

        if (!this.CanModify(post.AuthorId))
        {
            return this.Forbid();
        }

        post.IsDeleted = true;
        post.UpdatedAtUtc = DateTime.UtcNow;
        await this.db.SaveChangesAsync();
        this.TempData["Message"] = "Post deleted.";
        return this.Redirect($"/thread/{post.Thread.Slug}");
    }

    /// <summary>
    /// Member: report a problem post (spam, abuse, …) into the admin review queue.
    /// One pending report per member per post; the reason is optional.
    /// </summary>
    [HttpPost("report")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.CanPost)]
    public async Task<IActionResult> Report(int threadId, int postId, string? reason)
    {
        if (this.CurrentUserId() is not int uid)
        {
            return this.Challenge();
        }

        var post = await this.db.Posts.Include(p => p.Thread)
            .FirstOrDefaultAsync(p => p.Id == postId && p.ThreadId == threadId);
        if (post?.Thread is null || post.Thread.IsDeleted)
        {
            return this.NotFound();
        }

        // Don't stack duplicate pending reports from the same member on the same post.
        var already = await this.db.Reports.AnyAsync(r =>
            r.PostId == postId && r.ReporterUserId == uid && r.Status == ReportStatus.Pending);
        if (!already)
        {
            var report = new Report
            {
                ThreadId = threadId,
                PostId = postId,
                ReporterUserId = uid,
                Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
                Status = ReportStatus.Pending,
                CreatedAtUtc = DateTime.UtcNow,
            };
            this.db.Reports.Add(report);
            await this.db.SaveChangesAsync();

            // Alert every admin in-app (nav bell + notifications list).
            await this.notifications.NotifyReportAsync(report);
        }

        this.TempData["Message"] = "Thanks — this has been reported to the moderators for review.";
        return this.Redirect($"/thread/{post.Thread.Slug}");
    }

    /// <summary>
    /// Toggle a "thank" on a post. Approved members can thank any live post that
    /// isn't their own; thanking again removes it. One thank per member per post
    /// (enforced by a unique index; a race just no-ops). Redirects back to the post.
    /// </summary>
    [HttpPost("post/{id:int}/thank")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.CanPost)]
    public async Task<IActionResult> Thank(int id)
    {
        if (this.CurrentUserId() is not int uid)
        {
            return this.Challenge();
        }

        var post = await this.db.Posts.Include(p => p.Thread)
            .FirstOrDefaultAsync(p => p.Id == id);

        // Only live posts, and never your own, can be thanked.
        if (post?.Thread is null || post.Thread.IsDeleted || post.IsDeleted || post.IsCensored || post.AuthorId == uid)
        {
            return this.NotFound();
        }

        var existing = await this.db.PostThanks
            .FirstOrDefaultAsync(t => t.PostId == id && t.UserId == uid);

        if (existing is not null)
        {
            this.db.PostThanks.Remove(existing);
            await this.db.SaveChangesAsync();
        }
        else
        {
            this.db.PostThanks.Add(new PostThank
            {
                PostId = id,
                UserId = uid,
                PostAuthorId = post.AuthorId,
                CreatedAtUtc = DateTime.UtcNow,
            });
            try
            {
                await this.db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Double-click / two tabs raced the unique index — already thanked.
                this.db.ChangeTracker.Clear();
            }
        }

        var landingPage = await this.PageOfAsync(post);
        return this.Redirect($"/thread/{post.Thread.Slug}?page={landingPage}#post-{id}");
    }

    /// <summary>Which page (1-based) a post falls on, so a redirect lands on it.</summary>
    private async Task<int> PageOfAsync(Post post)
    {
        var earlier = await this.db.Posts.CountAsync(p => p.ThreadId == post.ThreadId && p.CreatedAtUtc < post.CreatedAtUtc);
        return (earlier / PageSize) + 1;
    }

    /// <summary>
    /// Live preview: render a draft body through the exact same markdown + sanitizer
    /// pipeline used for real posts, so a member can check formatting and links
    /// before submitting. Output is already sanitized, so it's safe to inject.
    /// </summary>
    [HttpPost("preview")]
    [ValidateAntiForgeryToken]
    [Authorize]
    public IActionResult Preview([FromForm] string? body)
        => this.Content(ForumMarkup.ToHtml(body), "text/html; charset=utf-8");

    /// <summary>
    /// Admin: censor a post to keep the community safe (threatening / dangerous
    /// content). Hides the post AND every reply beneath it. If the post is the
    /// thread's opening post, the entire thread is censored and hidden.
    /// </summary>
    [HttpPost("post/{id:int}/censor")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> CensorPost(int id)
    {
        var post = await this.db.Posts.Include(p => p.Thread).FirstOrDefaultAsync(p => p.Id == id);
        if (post?.Thread is null)
        {
            return this.NotFound();
        }

        if (post.IsOpeningPost)
        {
            // The opening post is the topic itself — censoring it hides the whole thread.
            post.Thread.IsCensored = true;
            await this.db.Posts.Where(p => p.ThreadId == post.ThreadId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsCensored, true));
            await this.db.SaveChangesAsync();
            this.TempData["Message"] = "Thread censored — hidden from members to keep the community safe.";
            return this.Redirect($"/thread/{post.Thread.Slug}");
        }

        var ids = await this.CollectReplySubtreeAsync(post.ThreadId, id);
        await this.db.Posts.Where(p => ids.Contains(p.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsCensored, true));

        var replies = ids.Count - 1;
        this.TempData["Message"] = replies > 0
            ? $"Post and {replies} repl{(replies == 1 ? "y" : "ies")} beneath it censored."
            : "Post censored.";
        return this.Redirect($"/thread/{post.Thread.Slug}");
    }

    /// <summary>Admin: reverse a censor — restore a post (and its reply subtree) or the whole thread.</summary>
    [HttpPost("post/{id:int}/uncensor")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> UncensorPost(int id)
    {
        var post = await this.db.Posts.Include(p => p.Thread).FirstOrDefaultAsync(p => p.Id == id);
        if (post?.Thread is null)
        {
            return this.NotFound();
        }

        if (post.IsOpeningPost && post.Thread.IsCensored)
        {
            post.Thread.IsCensored = false;
            await this.db.Posts.Where(p => p.ThreadId == post.ThreadId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsCensored, false));
            await this.db.SaveChangesAsync();
            this.TempData["Message"] = "Thread restored.";
            return this.Redirect($"/thread/{post.Thread.Slug}");
        }

        var ids = await this.CollectReplySubtreeAsync(post.ThreadId, id);
        await this.db.Posts.Where(p => ids.Contains(p.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsCensored, false));
        this.TempData["Message"] = "Post restored.";
        return this.Redirect($"/thread/{post.Thread.Slug}");
    }

    /// <summary>Admin: lift the censor on a whole thread (restore it and all its posts).</summary>
    [HttpPost("{id:int}/uncensor-thread")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> UncensorThread(int id)
    {
        var thread = await this.db.ForumThreads.FirstOrDefaultAsync(t => t.Id == id);
        if (thread is null)
        {
            return this.NotFound();
        }

        thread.IsCensored = false;
        await this.db.Posts.Where(p => p.ThreadId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsCensored, false));
        await this.db.SaveChangesAsync();
        this.TempData["Message"] = "Thread restored.";
        return this.Redirect($"/thread/{thread.Slug}");
    }

    /// <summary>The post plus every post that (transitively) replies to it, within one thread.</summary>
    private async Task<HashSet<int>> CollectReplySubtreeAsync(int threadId, int rootId)
    {
        var edges = await this.db.Posts
            .Where(p => p.ThreadId == threadId)
            .Select(p => new { p.Id, p.ReplyToPostId })
            .ToListAsync();

        var childrenByParent = edges
            .Where(e => e.ReplyToPostId != null)
            .GroupBy(e => e.ReplyToPostId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());

        var result = new HashSet<int> { rootId };
        var queue = new Queue<int>();
        queue.Enqueue(rootId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!childrenByParent.TryGetValue(current, out var children))
            {
                continue;
            }

            foreach (var child in children)
            {
                if (result.Add(child))
                {
                    queue.Enqueue(child);
                }
            }
        }

        return result;
    }

    /// <summary>Admin: permanently delete a thread and every post in it.</summary>
    [HttpPost("{id:int}/category")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> ChangeCategory(int id, ForumCategory category)
    {
        var thread = await this.db.ForumThreads.FirstOrDefaultAsync(t => t.Id == id);
        if (thread is null)
        {
            return this.NotFound();
        }

        if (category != ForumCategory.Unknown && thread.Category != category)
        {
            thread.Category = category;
            await this.db.SaveChangesAsync();
            this.TempData["Message"] = "Thread category updated.";
        }

        return this.RedirectToAction(nameof(this.View), new { slug = thread.Slug });
    }

    [HttpPost("{id:int}/delete-thread")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> DeleteThread(int id)
    {
        if (!await this.db.ForumThreads.AnyAsync(t => t.Id == id))
        {
            return this.NotFound();
        }

        // Clear intra-thread reply links first (ReplyToPost FK is Restrict), then hard-delete
        // the posts and the thread. DB cascades remove images, notifications, and subscriptions.
        await this.db.Posts.Where(p => p.ThreadId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.ReplyToPostId, (int?)null));
        await this.db.Posts.Where(p => p.ThreadId == id).ExecuteDeleteAsync();
        await this.db.ForumThreads.Where(t => t.Id == id).ExecuteDeleteAsync();

        this.TempData["Message"] = "Thread permanently deleted.";
        return this.RedirectToAction("Index", "Home");
    }

    private bool CanModify(int authorId)
    {
        var me = this.CurrentUserId();
        return (me == authorId) || this.User.IsInRole("Admin");
    }

    private int? CurrentUserId()
    {
        var id = this.users.GetUserId(this.User);
        return int.TryParse(id, out var i) ? i : null;
    }

    /// <summary>Whether the current member has held an approved account long enough to attach images.</summary>
    private async Task<bool> CurrentUserCanPostImagesAsync(CancellationToken ct = default)
    {
        if (this.CurrentUserId() is not int uid)
        {
            return false;
        }

        var u = await this.db.Users.AsNoTracking()
            .Select(x => new { x.Id, x.IsApproved, x.ApprovedAtUtc })
            .FirstOrDefaultAsync(x => x.Id == uid, ct);
        return u is not null && MemberPerks.CanPostImages(u.IsApproved, u.ApprovedAtUtc, DateTime.UtcNow);
    }

    /// <summary>Populates ViewBag.CanPostImages / ViewBag.ImageUnlockUtc for the create-thread view.</summary>
    private async Task SetImagePerkViewBagAsync()
    {
        var can = false;
        DateTime? unlock = null;
        if (this.CurrentUserId() is int uid)
        {
            var u = await this.db.Users.AsNoTracking()
                .Select(x => new { x.Id, x.IsApproved, x.ApprovedAtUtc })
                .FirstOrDefaultAsync(x => x.Id == uid);
            if (u is not null)
            {
                can = MemberPerks.CanPostImages(u.IsApproved, u.ApprovedAtUtc, DateTime.UtcNow);
                unlock = MemberPerks.ImagePostingUnlockUtc(u.IsApproved, u.ApprovedAtUtc);
            }
        }

        this.ViewBag.CanPostImages = can;
        this.ViewBag.ImageUnlockUtc = unlock;
    }

    /// <summary>
    /// Saves valid uploaded images and links them to <paramref name="post"/>. Server-side
    /// gate: silently drops everything unless the member's image-posting perk has unlocked
    /// (the compose UI only shows the picker to eligible members, but never trust the client).
    /// </summary>
    private async Task AttachImagesAsync(Post post, List<IFormFile>? images, DateTime now, CancellationToken ct = default)
    {
        if (images is null || images.Count == 0 || !await this.CurrentUserCanPostImagesAsync(ct))
        {
            return;
        }

        var order = 0;
        foreach (var file in images.Take(MaxPostImages))
        {
            if (file.Length <= 0 || file.Length > MaxPostImageBytes)
            {
                continue;
            }

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedImageExt.Contains(ext))
            {
                continue;
            }

            await using var stream = file.OpenReadStream();
            // Foldered per thread (threads/{threadId}) so a thread's images stay together;
            // the filename is a GUID (see BunnyImageStorage) so uploads never collide.
            var key = await this.images.SaveAsync(stream, $"threads/{post.ThreadId}", ext, ct);
            this.db.PostImages.Add(new PostImage
            {
                PostId = post.Id,
                StoragePath = key,
                SortOrder = order++,
                CreatedAtUtc = now,
            });
        }

        if (order > 0)
        {
            await this.db.SaveChangesAsync(ct);
        }
    }
}
