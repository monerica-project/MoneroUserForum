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
    // Threads shown in PUBLIC lists. A thread whose opening author isn't approved yet stays
    // hidden here (the author still reaches it via its URL, and admins can see it) — so a new
    // member's posts don't go live until their account is approved.
    private IQueryable<ForumThread> VisibleThreads()
        => this.db.ForumThreads.Where(t => !t.IsDeleted && !t.IsCensored && !t.Author!.IsDeleted && t.Author!.IsApproved);

    public async Task<List<ThreadListItem>> BuildListAsync(IQueryable<ForumThread> query, int page, string sort = "recent")
    {
        // Same sort options as the homepage: pinned first, then recent / most viewed / most replies.
        var pinned = query.OrderByDescending(t => t.IsPinned);
        var ordered = sort switch
        {
            "views" => pinned
                .ThenByDescending(t => t.Views.Count)
                .ThenByDescending(t => t.LastPostAtUtc),
            "replies" => pinned
                .ThenByDescending(t => t.Posts.Count(p => !p.IsOpeningPost && !p.Author!.IsDeleted && p.Author!.IsApproved))
                .ThenByDescending(t => t.LastPostAtUtc),
            _ => pinned.ThenByDescending(t => t.LastPostAtUtc),
        };

        var items = await ordered
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(t => new ThreadListItem
            {
                Title = t.Title,
                Slug = t.Slug,
                Category = t.Category,
                AuthorName = t.Author!.DisplayName,
                AuthorBanned = t.Author.IsBanned,
                // Counts / last-poster ignore posts by unapproved authors (their replies are hidden).
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
    public async Task<IActionResult> Category(ForumCategory category, int page = 1, string sort = "recent")
    {
        if (category == ForumCategory.Unknown)
        {
            return this.NotFound();
        }

        page = Math.Max(1, page);
        sort = sort == "views" || sort == "replies" ? sort : "recent";
        var items = await this.BuildListAsync(this.VisibleThreads().Where(t => t.Category == category), page, sort);
        return this.View(new ThreadListViewModel
        {
            Threads = items,
            Category = category,
            Heading = Support.ForumDisplay.Describe(category),
            Page = page,
            HasMore = items.Count == PageSize,
            Sort = sort,
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

        var meId = this.CurrentUserId();

        // A thread whose opening author isn't approved yet is "pending": visible only to that
        // author (so they can see their own post) and to admins. Everyone else gets a 404, just
        // like a hidden thread. Once the account is approved it appears for everyone automatically.
        var threadAuthorApproved = await this.db.Users
            .Where(u => u.Id == thread.AuthorId)
            .Select(u => u.IsApproved)
            .FirstOrDefaultAsync();
        var viewerIsThreadAuthor = meId == thread.AuthorId;
        if (!threadAuthorApproved && !viewerIsThreadAuthor && !isAdmin)
        {
            return this.NotFound();
        }

        var threadPending = !threadAuthorApproved;

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

        // Posts by an unapproved author are hidden from other members — but the author sees
        // their own (marked pending), and admins see everything. Keep the page count in sync.
        var totalPosts = await this.db.Posts.CountAsync(p =>
            p.ThreadId == thread.Id && (p.Author!.IsApproved || p.AuthorId == meId || isAdmin));
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalPosts / (double)PageSize));
        page = Math.Clamp(page, 1, totalPages);

        var raw = await this.db.Posts
            .Where(p => p.ThreadId == thread.Id && (p.Author!.IsApproved || p.AuthorId == meId || isAdmin))
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
                AuthorApproved = p.Author.IsApproved,
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

        // Thanks each of this page's authors has GIVEN out (grouped by the giver, UserId).
        var thanksGiven = await this.db.PostThanks
            .Where(t => authorIds.Contains(t.UserId))
            .GroupBy(t => t.UserId)
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
            AuthorThanksGiven = thanksGiven.GetValueOrDefault(r.AuthorId),
            ThankCount = thankCounts.GetValueOrDefault(r.Id),
            ViewerThanked = myThanks.Contains(r.Id),
            CanThank = viewerCanThank && meId != r.AuthorId && !r.IsDeleted && !r.IsCensored && !r.AuthorBanned && !r.AuthorDeleted,
            AuthorBanned = r.AuthorBanned,
            AuthorDeleted = r.AuthorDeleted,
            Body = r.Body,
            IsDeleted = r.IsDeleted,
            IsCensored = r.IsCensored,
            IsPending = !r.AuthorApproved,
            IsOpeningPost = r.IsOpeningPost,
            ReplyToPostId = r.ReplyToPostId,
            ReplyToAuthorName = r.ReplyToAuthorName,
            CreatedAtUtc = r.CreatedAtUtc,
            Edited = r.UpdatedAtUtc > r.CreatedAtUtc.AddSeconds(5),
            CanModify = !r.AuthorDeleted && ((meId == r.AuthorId) || isAdmin),
            CanEdit = !r.AuthorDeleted && meId == r.AuthorId,
            CanCensor = isAdmin,
            CanHardDelete = isAdmin && !r.IsOpeningPost,
            ImageUrls = r.Images.Select(i => this.images.PublicUrl(i)).ToList(),
        }).ToList();

        // Notifications for new replies are ON by default for anyone who has posted in the
        // thread (or explicitly followed it) — unless they've muted (unsubscribed from) it.
        var following = false;
        if (meId is int uid)
        {
            var sub = await this.db.ThreadSubscriptions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.ThreadId == thread.Id && s.UserId == uid);
            var isMuted = sub is { IsMuted: true };
            var isParticipant = await this.db.Posts.AnyAsync(p => p.ThreadId == thread.Id && p.AuthorId == uid);
            following = !isMuted && (isParticipant || sub is { IsMuted: false });
        }

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
            ThreadPending = threadPending,
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

    // Toggle per-thread reply notifications for the current user. ON by default (posting in a
    // thread opts you in); this flips between subscribed and unsubscribed (muted).
    [Authorize]
    [HttpPost("{slug}/notifications")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleNotifications(string slug)
    {
        var thread = await this.db.ForumThreads.FirstOrDefaultAsync(t => t.Slug == slug && !t.IsDeleted);
        if (thread is null || this.CurrentUserId() is not int uid)
        {
            return this.NotFound();
        }

        var sub = await this.db.ThreadSubscriptions
            .FirstOrDefaultAsync(s => s.ThreadId == thread.Id && s.UserId == uid);
        var isMuted = sub is { IsMuted: true };
        var isParticipant = await this.db.Posts.AnyAsync(p => p.ThreadId == thread.Id && p.AuthorId == uid);
        var isOn = !isMuted && (isParticipant || sub is { IsMuted: false });

        // If notifications are currently ON, this turns them OFF (mute); otherwise turn ON.
        var wantMuted = isOn;
        if (sub is null)
        {
            this.db.ThreadSubscriptions.Add(new ThreadSubscription
            {
                ThreadId = thread.Id,
                UserId = uid,
                IsMuted = wantMuted,
                CreatedAtUtc = DateTime.UtcNow,
            });
        }
        else
        {
            sub.IsMuted = wantMuted;
        }

        await this.db.SaveChangesAsync();
        this.TempData["Message"] = wantMuted
            ? "Unsubscribed — you won't be notified of new replies in this thread."
            : "Subscribed — you'll be notified of new replies in this thread.";
        return this.RedirectToAction(nameof(this.View), new { slug = thread.Slug });
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
    [Authorize(Policy = Policies.CanContribute)]
    public async Task<IActionResult> Create(ForumCategory category = ForumCategory.GeneralDiscussion)
    {
        await this.SetImagePerkViewBagAsync();
        return this.View(new CreateThreadViewModel { Category = category == ForumCategory.Unknown ? ForumCategory.GeneralDiscussion : category });
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.CanContribute)]
    public async Task<IActionResult> Create(CreateThreadViewModel model, List<IFormFile>? images)
    {
        await this.SetImagePerkViewBagAsync();
        if (model.Category == ForumCategory.Unknown)
        {
            this.ModelState.AddModelError(nameof(model.Category), "Choose a category.");
        }

        // Promotional threads are rate-limited to one per member every 30 days (admins exempt),
        // so the forum can't be flooded with ads. Counts every promo thread by the author in the
        // window — including ones they later deleted — so delete-and-repost can't dodge the limit.
        if (model.Category == ForumCategory.Promotional && !this.User.IsInRole("Admin"))
        {
            var meId = this.CurrentUserId()!.Value;
            var cutoff = DateTime.UtcNow.AddDays(-30);
            var alreadyPosted = await this.db.ForumThreads.AnyAsync(t =>
                t.AuthorId == meId && t.Category == ForumCategory.Promotional && t.CreatedAtUtc >= cutoff);
            if (alreadyPosted)
            {
                this.ModelState.AddModelError(nameof(model.Category),
                    "You can post only one Promotional thread every 30 days. Please try again later.");
            }
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

        // Notify subscribers/mentions only about posts that are actually visible. An unapproved
        // author's thread is pending, so tell THEM instead of blasting a hidden post.
        if (this.User.HasClaim("approved", "true"))
        {
            await this.notifications.NotifyOnNewPostAsync(opening);
        }
        else
        {
            this.TempData["Message"] = "Your thread was posted. It stays hidden from other members "
                + "until an admin approves your account — you can keep posting in the meantime.";
        }

        return this.RedirectToAction(nameof(this.View), new { slug = thread.Slug });
    }

    [HttpPost("reply")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.CanContribute)]
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

        // Hidden (pending) replies don't notify anyone — tell the author instead.
        if (this.User.HasClaim("approved", "true"))
        {
            await this.notifications.NotifyOnNewPostAsync(post);
        }
        else
        {
            this.TempData["Message"] = "Your reply was posted. It stays hidden from other members "
                + "until an admin approves your account — you can keep posting in the meantime.";
        }

        var lastPage = Math.Max(1, (int)Math.Ceiling(
            await this.db.Posts.CountAsync(p => p.ThreadId == thread.Id) / (double)PageSize));
        return this.Redirect($"/thread/{thread.Slug}?page={lastPage}#post-{post.Id}");
    }

    [HttpGet("post/{id:int}/edit")]
    [Authorize(Policy = Policies.CanContribute)]
    public async Task<IActionResult> EditPost(int id)
    {
        // A soft-deleted post is still editable by its author (editing brings it back with their
        // new text). A moderator-censored post is not — that stays locked. See the POST below.
        var post = await this.db.Posts.Include(p => p.Thread).FirstOrDefaultAsync(p => p.Id == id && !p.IsCensored);
        if (post?.Thread is null)
        {
            return this.NotFound();
        }

        if (!this.IsAuthor(post.AuthorId))
        {
            return this.Forbid();
        }

        return this.View(new EditPostViewModel
        {
            PostId = post.Id,
            ThreadSlug = post.Thread.Slug,
            Body = post.Body,
            IsOpeningPost = post.IsOpeningPost,
            Title = post.IsOpeningPost ? post.Thread.Title : null,
        });
    }

    [HttpPost("post/{id:int}/edit")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.CanContribute)]
    public async Task<IActionResult> EditPost(int id, EditPostViewModel model)
    {
        var post = await this.db.Posts.Include(p => p.Thread).FirstOrDefaultAsync(p => p.Id == id && !p.IsCensored);
        if (post?.Thread is null)
        {
            return this.NotFound();
        }

        if (!this.IsAuthor(post.AuthorId))
        {
            return this.Forbid();
        }

        // The title only applies to a thread's opening post; require a sensible one there.
        if (post.IsOpeningPost && (model.Title ?? string.Empty).Trim().Length < 4)
        {
            this.ModelState.AddModelError(nameof(model.Title), "The title must be at least 4 characters.");
        }

        if (!this.ModelState.IsValid)
        {
            model.PostId = post.Id;
            model.ThreadSlug = post.Thread.Slug;
            model.IsOpeningPost = post.IsOpeningPost;
            return this.View(model);
        }

        post.Body = model.Body.Trim();
        post.UpdatedAtUtc = DateTime.UtcNow;

        // If the author had deleted this post, editing it brings it back (with their new text /
        // added comment). Moderator-censored posts never reach here, so this can't undo moderation.
        post.IsDeleted = false;

        // Editing the opening post can also rename the thread — and the slug (URL) follows.
        if (post.IsOpeningPost && !string.IsNullOrWhiteSpace(model.Title))
        {
            var newTitle = model.Title.Trim();
            post.Thread.Title = newTitle;
            post.Thread.UpdatedAtUtc = DateTime.UtcNow;

            var slug = SlugHelper.Slugify(newTitle);
            if (string.IsNullOrEmpty(slug))
            {
                slug = "thread";
            }

            if (slug != post.Thread.Slug)
            {
                // Keep it unique, ignoring this thread's own current slug.
                var baseSlug = slug;
                var n = 1;
                while (await this.db.ForumThreads.AnyAsync(t => t.Slug == slug && t.Id != post.Thread.Id))
                {
                    slug = $"{baseSlug}-{++n}";
                }

                post.Thread.Slug = slug;
            }
        }

        await this.db.SaveChangesAsync();
        return this.Redirect($"/thread/{post.Thread.Slug}#post-{post.Id}");
    }

    [HttpPost("post/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.CanContribute)]
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
        this.TempData["Message"] = "Post deleted. You can bring it back any time by editing it.";
        return this.Redirect($"/thread/{post.Thread.Slug}");
    }

    /// <summary>
    /// Admin: permanently remove a single reply. Unlike a normal delete (which only hides the post
    /// and can be undone by the author), this erases it for good. The opening post can't be removed
    /// on its own — deleting that means deleting the whole thread.
    /// </summary>
    [HttpPost("post/{id:int}/hard-delete")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> HardDeletePost(int id)
    {
        var post = await this.db.Posts.Include(p => p.Thread).FirstOrDefaultAsync(p => p.Id == id);
        if (post?.Thread is null)
        {
            return this.NotFound();
        }

        var slug = post.Thread.Slug;
        if (post.IsOpeningPost)
        {
            this.TempData["Error"] = "The opening post can't be removed on its own — delete the whole thread instead.";
            return this.Redirect($"/thread/{slug}");
        }

        // ReplyToPost is a Restrict FK, so first detach any replies that point at this post; the
        // remaining relations (thanks, images, notifications, reports) cascade on delete.
        await this.db.Posts.Where(p => p.ReplyToPostId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.ReplyToPostId, (int?)null));
        await this.db.Posts.Where(p => p.Id == id).ExecuteDeleteAsync();

        this.TempData["Message"] = "Post permanently deleted.";
        return this.Redirect($"/thread/{slug}");
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

            // Un-thanked: drop the matching notification so it doesn't linger in the author's bell.
            await this.db.Notifications
                .Where(n => n.Type == NotificationType.Thanked && n.PostId == id
                            && n.ActorUserId == uid && n.RecipientUserId == post.AuthorId)
                .ExecuteDeleteAsync();
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
            var thanked = false;
            try
            {
                await this.db.SaveChangesAsync();
                thanked = true;
            }
            catch (DbUpdateException)
            {
                // Double-click / two tabs raced the unique index — already thanked.
                this.db.ChangeTracker.Clear();
            }

            // Tell the author who thanked them, linked to the exact post.
            if (thanked)
            {
                await this.notifications.NotifyThankAsync(post, uid);
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

    /// <summary>Admin: lock/unlock a thread. A locked thread accepts no new replies
    /// (the reply action already rejects them); this just flips the flag.</summary>
    [HttpPost("{id:int}/lock")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<IActionResult> SetLock(int id, bool locked)
    {
        var thread = await this.db.ForumThreads.FirstOrDefaultAsync(t => t.Id == id);
        if (thread is null)
        {
            return this.NotFound();
        }

        if (thread.IsLocked != locked)
        {
            thread.IsLocked = locked;
            thread.UpdatedAtUtc = DateTime.UtcNow;
            await this.db.SaveChangesAsync();
        }

        this.TempData["Message"] = locked
            ? "Thread locked — no new replies allowed."
            : "Thread unlocked.";
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

    // Editing a post is author-only — an admin editing someone else's words would only ever be an
    // accident. Admins still moderate via delete / censor (which use CanModify above).
    private bool IsAuthor(int authorId) => this.CurrentUserId() == authorId;

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
