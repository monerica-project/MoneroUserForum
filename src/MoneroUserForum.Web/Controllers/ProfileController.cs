using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MoneroUserForum.Data;
using MoneroUserForum.Data.Identity;
using MoneroUserForum.Services.Pgp;
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

        // Counts only posts others can see (not censored, in a live public thread).
        var postCount = await this.db.Posts.CountAsync(p => p.AuthorId == user.Id && !p.IsDeleted && !p.IsCensored
            && !p.Thread!.IsDeleted && !p.Thread.IsCensored && p.Thread.Author!.IsApproved);
        // Thanks this member has been given ON their posts (received) vs. thanks they handed out (given).
        var thanksReceived = await this.db.PostThanks.CountAsync(t => t.PostAuthorId == user.Id);
        var thanksGiven = await this.db.PostThanks.CountAsync(t => t.UserId == user.Id);
        var meId = this.users.GetUserId(this.User);

        // An account nobody has approved yet has no public profile: otherwise a fresh sign-up could
        // publish links, a signature and post snippets without ever passing approval.
        if (!user.IsApproved && meId != user.Id.ToString() && !this.User.IsInRole(Roles.Admin))
        {
            return this.NotFound();
        }

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

        // A banned member's promotion (links, signature, donation address) is hidden from everyone
        // but admins, the same way their posts are.
        var hideExtras = user.IsBanned && !this.User.IsInRole(Roles.Admin);

        var model = new ProfileViewModel
        {
            UserId = user.Id,
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
            PgpPublicKey = user.PgpVerifiedAtUtc is not null || meId == user.Id.ToString() ? user.PgpPublicKey : null,
            PgpFingerprint = user.PgpFingerprint,
            PgpVerifiedAtUtc = user.PgpVerifiedAtUtc,
            Signature = user.Signature,
            MoneroDonationAddress = user.MoneroDonationAddress,
            MoneroQrDataUri = QrCodeHelper.MoneroTipDataUri(user.MoneroDonationAddress),
            Link1Label = user.Link1Label,
            Link1Url = user.Link1Url,
            Link2Label = user.Link2Label,
            Link2Url = user.Link2Url,
            Link3Label = user.Link3Label,
            Link3Url = user.Link3Url,
        };

        if (hideExtras)
        {
            model.WebsiteUrl = model.TwitterUrl = model.SimpleXAddress = model.TelegramHandle = model.MonericaUrl = null;
            model.Signature = model.MoneroDonationAddress = model.MoneroQrDataUri = model.PgpPublicKey = null;
            model.Link1Label = model.Link1Url = model.Link2Label = model.Link2Url = model.Link3Label = model.Link3Url = null;
            model.AvatarUrl = null;
        }

        return this.View(model);
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

        PgpKeyInfo? pgp = null;
        if (!string.IsNullOrWhiteSpace(model.PgpPublicKey)
            && !string.Equals(NormalizeKey(model.PgpPublicKey), NormalizeKey(user.PgpPublicKey), StringComparison.Ordinal))
        {
            pgp = PgpTools.ReadPublicKey(model.PgpPublicKey, out var pgpError);
            if (pgp is null)
            {
                this.ModelState.AddModelError(nameof(model.PgpPublicKey), pgpError!);
            }
        }

        // Adding, changing or removing the PGP key needs the current password, like changing the
        // email does: messages to this member are encrypted to that key.
        var pgpKeyChanging = !string.Equals(NormalizeKey(Trim(model.PgpPublicKey)), NormalizeKey(user.PgpPublicKey), StringComparison.Ordinal);
        if (pgpKeyChanging
            && (string.IsNullOrEmpty(model.CurrentPassword) || !await this.users.CheckPasswordAsync(user, model.CurrentPassword)))
        {
            this.ModelState.AddModelError(nameof(model.CurrentPassword), "Enter your current password to change your PGP key.");
        }

        if (!this.ModelState.IsValid)
        {
            model.CurrentAvatarUrl = string.IsNullOrEmpty(user.AvatarPath) ? null : this.images.PublicUrl(user.AvatarPath);
            model.PgpFingerprint = user.PgpFingerprint;
            model.PgpVerifiedAtUtc = user.PgpVerifiedAtUtc;
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

            // Shrink to a small WebP and strip metadata (GPS etc.) before it's published.
            var processed = ImageProcessor.Process(await FormFileBytes.ReadAllAsync(avatar), ImagePurpose.Avatar);
            if (!processed.Ok)
            {
                this.TempData["Error"] = processed.Error;
                return this.RedirectToAction(nameof(this.Edit));
            }

            var old = user.AvatarPath;
            using var stream = new MemoryStream(processed.Bytes!);
            // Foldered per user under profile/ so each member's avatars stay grouped; the
            // filename itself is a GUID (see BunnyImageStorage) so uploads never collide.
            user.AvatarPath = await this.images.SaveAsync(stream, $"profile/{user.Id}", processed.Extension!);
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
        // A new or changed key must be verified again before others can see it or message you.
        var newKey = Trim(model.PgpPublicKey);
        var keyChanged = !string.Equals(NormalizeKey(newKey), NormalizeKey(user.PgpPublicKey), StringComparison.Ordinal);
        if (keyChanged)
        {
            user.PgpPublicKey = newKey;
            user.PgpFingerprint = pgp?.Fingerprint;
            user.PgpVerifiedAtUtc = null;
            ClearChallenge(user);
        }
        user.MoneroDonationAddress = Trim(model.MoneroDonationAddress);
        user.Link1Label = Trim(model.Link1Label);
        user.Link1Url = Trim(model.Link1Url);
        user.Link2Label = Trim(model.Link2Label);
        user.Link2Url = Trim(model.Link2Url);
        user.Link3Label = Trim(model.Link3Label);
        user.Link3Url = Trim(model.Link3Url);
        user.UpdatedAtUtc = DateTime.UtcNow;

        await this.users.UpdateAsync(user);
        if (keyChanged && newKey is not null)
        {
            this.TempData["Message"] = "Profile updated. Now verify your PGP key so others can see it and message you.";
            return this.RedirectToAction(nameof(this.Pgp));
        }

        this.TempData["Message"] = "Profile updated.";
        return this.RedirectToAction(nameof(this.View), new { name = user.DisplayName });
    }

    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromHours(1);

    /// <summary>
    /// PGP key verification: we encrypt a one-time code to the member's key; they decrypt it with
    /// their private key and type it back, proving they hold the key. The code is kept only as a hash.
    /// </summary>
    [HttpGet("pgp")]
    [Authorize]
    public async Task<IActionResult> Pgp()
    {
        var user = await this.users.GetUserAsync(this.User);
        if (user is null)
        {
            return this.Challenge();
        }

        if (!string.IsNullOrEmpty(user.PgpPublicKey) && user.PgpVerifiedAtUtc is null
            && (user.PgpChallengeExpiresUtc is null || user.PgpChallengeExpiresUtc < DateTime.UtcNow || string.IsNullOrEmpty(user.PgpChallengeArmored)))
        {
            if (!this.NewChallenge(user))
            {
                this.TempData["Error"] = "Your saved PGP key can no longer receive encrypted messages. Paste an updated key.";
                return this.RedirectToAction(nameof(this.Edit));
            }

            await this.users.UpdateAsync(user);
        }

        return this.View(this.ToPgpModel(user));
    }

    [HttpPost("pgp/new-code")]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> PgpNewCode()
    {
        var user = await this.users.GetUserAsync(this.User);
        if (user is null)
        {
            return this.Challenge();
        }

        if (!string.IsNullOrEmpty(user.PgpPublicKey) && user.PgpVerifiedAtUtc is null && this.NewChallenge(user))
        {
            await this.users.UpdateAsync(user);
        }

        return this.RedirectToAction(nameof(this.Pgp));
    }

    [HttpPost("pgp/verify")]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> PgpVerify(string? code)
    {
        var user = await this.users.GetUserAsync(this.User);
        if (user is null)
        {
            return this.Challenge();
        }

        if (string.IsNullOrEmpty(user.PgpPublicKey) || user.PgpVerifiedAtUtc is not null)
        {
            return this.RedirectToAction(nameof(this.Pgp));
        }

        if (user.PgpChallengeExpiresUtc is null || user.PgpChallengeExpiresUtc < DateTime.UtcNow)
        {
            this.TempData["Error"] = "That code expired. Here's a new one.";
            return this.RedirectToAction(nameof(this.Pgp));
        }

        if (!PgpTools.CodeMatches(code, user.PgpChallengeHash))
        {
            var model = this.ToPgpModel(user);
            model.Error = "That code doesn't match. Decrypt the message above with your private key and enter the code inside it.";
            return this.View("Pgp", model);
        }

        user.PgpVerifiedAtUtc = DateTime.UtcNow;
        ClearChallenge(user);
        user.UpdatedAtUtc = DateTime.UtcNow;
        await this.users.UpdateAsync(user);
        this.TempData["Message"] = "PGP key verified. Other members can now see it and send you encrypted messages.";
        return this.RedirectToAction(nameof(this.View), new { name = user.DisplayName });
    }

    private bool NewChallenge(AppUser user)
    {
        var code = PgpTools.NewChallengeCode();
        try
        {
            user.PgpFingerprint ??= PgpTools.ReadPublicKey(user.PgpPublicKey, out _)?.Fingerprint;
            user.PgpChallengeArmored = PgpTools.EncryptTo(user.PgpPublicKey!,
                $"Monero User Forum PGP key verification for {user.DisplayName}.\n\nYour code: {code}\n");
        }
        catch
        {
            return false;
        }

        user.PgpChallengeHash = PgpTools.HashCode(code);
        user.PgpChallengeExpiresUtc = DateTime.UtcNow.Add(ChallengeLifetime);
        return true;
    }

    private static void ClearChallenge(AppUser user)
    {
        user.PgpChallengeHash = null;
        user.PgpChallengeArmored = null;
        user.PgpChallengeExpiresUtc = null;
    }

    private PgpVerifyViewModel ToPgpModel(AppUser user) => new()
    {
        DisplayName = user.DisplayName,
        HasKey = !string.IsNullOrEmpty(user.PgpPublicKey),
        Fingerprint = user.PgpFingerprint,
        VerifiedAtUtc = user.PgpVerifiedAtUtc,
        ChallengeArmored = user.PgpChallengeArmored,
        ChallengeExpiresUtc = user.PgpChallengeExpiresUtc,
    };

    private static string NormalizeKey(string? key) =>
        string.Join("\n", (key ?? string.Empty).Replace("\r", string.Empty).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));

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
        PgpFingerprint = user.PgpFingerprint,
        PgpVerifiedAtUtc = user.PgpVerifiedAtUtc,
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
