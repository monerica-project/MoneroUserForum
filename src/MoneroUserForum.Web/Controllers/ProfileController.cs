using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MoneroUserForum.Data;
using MoneroUserForum.Data.Identity;
using MoneroUserForum.Services.Reputation;
using MoneroUserForum.Services.Storage;
using MoneroUserForum.Web.Auth;
using MoneroUserForum.Web.Models.Profile;
using MoneroUserForum.Web.Support;

namespace MoneroUserForum.Web.Controllers;

[Route("profile")]
public class ProfileController : Controller
{
    private static readonly Dictionary<string, string> AllowedImages = new()
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif",
    };

    private readonly AppDbContext db;
    private readonly UserManager<AppUser> users;
    private readonly IImageStorage images;

    public ProfileController(AppDbContext db, UserManager<AppUser> users, IImageStorage images)
    {
        this.db = db;
        this.users = users;
        this.images = images;
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var user = await this.users.GetUserAsync(this.User);
        return user is null ? this.Challenge() : this.RedirectToAction(nameof(this.View), new { name = user.DisplayName });
    }

    private const int ProfilePostsPageSize = 10;

    [HttpGet("{name}")]
    public async Task<IActionResult> View(string name, int page = 1)
    {
        var normalized = name.Trim().ToLowerInvariant();
        var user = await this.users.FindByNameAsync(normalized);
        if (user is null || user.IsDeleted)
        {
            return this.NotFound();
        }

        var postCount = await this.db.Posts.CountAsync(p => p.AuthorId == user.Id && !p.IsDeleted);
        // Thanks this member has been given ON their posts (received) vs. thanks they handed out (given).
        var thanksReceived = await this.db.PostThanks.CountAsync(t => t.PostAuthorId == user.Id);
        var thanksGiven = await this.db.PostThanks.CountAsync(t => t.UserId == user.Id);
        var meId = this.users.GetUserId(this.User);

        // This member's threads and replies, newest first, paged. Only publicly-visible content
        // (not deleted, not censored, in a live thread).
        var postsQuery = this.db.Posts
            .Where(p => p.AuthorId == user.Id && !p.IsDeleted && !p.IsCensored
                && !p.Thread!.IsDeleted && !p.Thread!.IsCensored);
        var visiblePosts = await postsQuery.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(visiblePosts / (double)ProfilePostsPageSize));
        page = Math.Clamp(page, 1, totalPages);
        var recentPosts = await postsQuery
            .OrderByDescending(p => p.CreatedAtUtc)
            .Skip((page - 1) * ProfilePostsPageSize)
            .Take(ProfilePostsPageSize)
            .Select(p => new ProfilePostItem
            {
                PostId = p.Id,
                ThreadSlug = p.Thread!.Slug,
                ThreadTitle = p.Thread!.Title,
                IsOpeningPost = p.IsOpeningPost,
                CreatedAtUtc = p.CreatedAtUtc,
                Snippet = p.Body.Length > 180 ? p.Body.Substring(0, 180) : p.Body,
            })
            .ToListAsync();

        return this.View(new ProfileViewModel
        {
            DisplayName = user.DisplayName,
            AvatarUrl = string.IsNullOrEmpty(user.AvatarPath) ? null : this.images.PublicUrl(user.AvatarPath),
            JoinedAtUtc = user.CreatedAtUtc,
            PostCount = postCount,
            ThanksReceived = thanksReceived,
            ThanksGiven = thanksGiven,
            RecentPosts = recentPosts,
            Page = page,
            TotalPages = totalPages,
            Reputation = ReputationCalculator.Compute(user.CreatedAtUtc, postCount, DateTime.UtcNow),
            IsBanned = user.IsBanned,
            IsSelf = meId == user.Id.ToString(),
            WebsiteUrl = user.WebsiteUrl,
            TwitterUrl = user.TwitterUrl,
            SimpleXAddress = user.SimpleXAddress,
            TelegramHandle = user.TelegramHandle,
            MonericaUrl = user.MonericaUrl,
            PgpPublicKey = user.PgpPublicKey,
            Signature = user.Signature,
            MoneroDonationAddress = user.MoneroDonationAddress,
            MoneroQrDataUri = QrCodeHelper.MoneroTipDataUri(user.MoneroDonationAddress),
            Link1Label = user.Link1Label,
            Link1Url = user.Link1Url,
            Link2Label = user.Link2Label,
            Link2Url = user.Link2Url,
            Link3Label = user.Link3Label,
            Link3Url = user.Link3Url,
        });
    }

    private const int ThanksPageSize = 20;

    /// <summary>Lists the actual posts behind a member's "thanks received" / "thanks given" totals,
    /// so it's clear which posts were thanked and by/for whom.</summary>
    [HttpGet("{name}/thanks")]
    public async Task<IActionResult> Thanks(string name, string type = "received", int page = 1)
    {
        var normalized = name.Trim().ToLowerInvariant();
        var user = await this.users.FindByNameAsync(normalized);
        if (user is null || user.IsDeleted)
        {
            return this.NotFound();
        }

        var received = !string.Equals(type, "given", StringComparison.OrdinalIgnoreCase);

        // received → thanks on THIS member's posts; given → thanks THIS member handed out.
        // Restrict to still-visible posts/threads.
        var baseQuery = this.db.PostThanks
            .Where(t => received ? t.PostAuthorId == user.Id : t.UserId == user.Id)
            .Where(t => !t.Post!.IsDeleted && !t.Post!.IsCensored
                && !t.Post!.Thread!.IsDeleted && !t.Post!.Thread!.IsCensored
                && t.Post!.Author!.IsApproved && !t.Post!.Author!.IsDeleted); // only publicly-visible posts

        var total = await baseQuery.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)ThanksPageSize));
        page = Math.Clamp(page, 1, totalPages);

        var items = await baseQuery
            .OrderByDescending(t => t.CreatedAtUtc)
            .Skip((page - 1) * ThanksPageSize)
            .Take(ThanksPageSize)
            .Select(t => new ThanksListItem
            {
                PostId = t.PostId,
                ThreadSlug = t.Post!.Thread!.Slug,
                ThreadTitle = t.Post!.Thread!.Title,
                IsOpeningPost = t.Post!.IsOpeningPost,
                ThreadPage = (this.db.Posts.Count(p => p.ThreadId == t.Post!.ThreadId && p.CreatedAtUtc < t.Post!.CreatedAtUtc) / 20) + 1, // 20 = thread posts-per-page
                OtherName = received ? t.User!.DisplayName : t.Post!.Author!.DisplayName,
                WhenUtc = t.CreatedAtUtc,
            })
            .ToListAsync();

        return this.View(new ThanksListViewModel
        {
            DisplayName = user.DisplayName,
            IsReceived = received,
            Total = total,
            Items = items,
            Page = page,
            TotalPages = totalPages,
        });
    }

    [HttpGet("edit")]
    [Authorize]
    public async Task<IActionResult> Edit()
    {
        var user = await this.users.GetUserAsync(this.User);
        if (user is null)
        {
            return this.Challenge();
        }

        return this.View(this.ToEditModel(user));
    }

    [HttpPost("edit")]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> Edit(ProfileEditViewModel model, IFormFile? avatar)
    {
        var user = await this.users.GetUserAsync(this.User);
        if (user is null)
        {
            return this.Challenge();
        }

        // The Monerica link is specifically the member's own monerica.com listing, so
        // require it to actually point at monerica.com (any subdomain).
        if (!string.IsNullOrWhiteSpace(model.MonericaUrl)
            && (!Uri.TryCreate(model.MonericaUrl.Trim(), UriKind.Absolute, out var mUri)
                || !(mUri.Host.Equals("monerica.com", StringComparison.OrdinalIgnoreCase)
                     || mUri.Host.EndsWith(".monerica.com", StringComparison.OrdinalIgnoreCase))))
        {
            this.ModelState.AddModelError(nameof(model.MonericaUrl), "Enter a link on monerica.com (e.g. https://monerica.com/site/your-listing).");
        }

        if (!this.ModelState.IsValid)
        {
            model.CurrentAvatarUrl = string.IsNullOrEmpty(user.AvatarPath) ? null : this.images.PublicUrl(user.AvatarPath);
            return this.View(model);
        }

        // Avatar upload (approved members only, to prevent spam images).
        if (avatar is { Length: > 0 })
        {
            if (!User.HasClaim("approved", "true") || User.HasClaim("banned", "true"))
            {
                this.TempData["Error"] = "Your account must be approved before you can upload an image.";
                return this.RedirectToAction(nameof(this.Edit));
            }

            var ext = Path.GetExtension(avatar.FileName).ToLowerInvariant();
            if (!AllowedImages.ContainsKey(ext))
            {
                this.TempData["Error"] = "Avatar must be a JPG, PNG, WEBP, or GIF.";
                return this.RedirectToAction(nameof(this.Edit));
            }

            if (avatar.Length > 3 * 1024 * 1024)
            {
                this.TempData["Error"] = "Avatar must be under 3 MB.";
                return this.RedirectToAction(nameof(this.Edit));
            }

            var old = user.AvatarPath;
            await using var stream = avatar.OpenReadStream();
            // Foldered per user under profile/ so each member's avatars stay grouped; the
            // filename itself is a GUID (see BunnyImageStorage) so uploads never collide.
            user.AvatarPath = await this.images.SaveAsync(stream, $"profile/{user.Id}", ext);
            if (!string.IsNullOrEmpty(old))
            {
                await this.images.DeleteAsync(old);
            }
        }

        user.Signature = Trim(model.Signature);
        user.WebsiteUrl = Trim(model.WebsiteUrl);
        user.TwitterUrl = Trim(model.TwitterUrl);
        user.SimpleXAddress = Trim(model.SimpleXAddress);
        user.TelegramHandle = Trim(model.TelegramHandle);
        user.MonericaUrl = Trim(model.MonericaUrl);
        user.PgpPublicKey = Trim(model.PgpPublicKey);
        user.MoneroDonationAddress = Trim(model.MoneroDonationAddress);
        user.Link1Label = Trim(model.Link1Label);
        user.Link1Url = Trim(model.Link1Url);
        user.Link2Label = Trim(model.Link2Label);
        user.Link2Url = Trim(model.Link2Url);
        user.Link3Label = Trim(model.Link3Label);
        user.Link3Url = Trim(model.Link3Url);
        user.UpdatedAtUtc = DateTime.UtcNow;

        await this.users.UpdateAsync(user);
        this.TempData["Message"] = "Profile updated.";
        return this.RedirectToAction(nameof(this.View), new { name = user.DisplayName });
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private ProfileEditViewModel ToEditModel(AppUser user) => new()
    {
        Signature = user.Signature,
        WebsiteUrl = user.WebsiteUrl,
        TwitterUrl = user.TwitterUrl,
        SimpleXAddress = user.SimpleXAddress,
        TelegramHandle = user.TelegramHandle,
        MonericaUrl = user.MonericaUrl,
        PgpPublicKey = user.PgpPublicKey,
        MoneroDonationAddress = user.MoneroDonationAddress,
        Link1Label = user.Link1Label,
        Link1Url = user.Link1Url,
        Link2Label = user.Link2Label,
        Link2Url = user.Link2Url,
        Link3Label = user.Link3Label,
        Link3Url = user.Link3Url,
        CurrentAvatarUrl = string.IsNullOrEmpty(user.AvatarPath) ? null : this.images.PublicUrl(user.AvatarPath),
    };
}
