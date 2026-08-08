using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using MoneroUserForum.Data;
using MoneroUserForum.Data.Identity;
using MoneroUserForum.Services.Email;
using MoneroUserForum.Services.Reputation;
using MoneroUserForum.Web.Models.Account;

namespace MoneroUserForum.Web.Controllers;

[Route("account")]
public class AccountController : Controller
{
    private readonly UserManager<AppUser> users;
    private readonly SignInManager<AppUser> signIn;
    private readonly IEmailSender email;
    private readonly AppDbContext db;

    public AccountController(UserManager<AppUser> users, SignInManager<AppUser> signIn, IEmailSender email, AppDbContext db)
    {
        this.users = users;
        this.signIn = signIn;
        this.email = email;
        this.db = db;
    }

    /// <summary>Member-facing page: which features are unlocked and, for the time-gated ones, when.</summary>
    [HttpGet("unlocks")]
    [Authorize]
    public async Task<IActionResult> Unlocks()
    {
        var user = await this.users.GetUserAsync(this.User);
        if (user is null)
        {
            return this.Challenge();
        }

        var now = DateTime.UtcNow;
        var postCount = await this.db.Posts.CountAsync(p => p.AuthorId == user.Id && !p.IsDeleted);

        return this.View(new MemberUnlocksViewModel
        {
            IsApproved = user.IsApproved,
            ApprovedAtUtc = user.ApprovedAtUtc,
            CreatedAtUtc = user.CreatedAtUtc,
            NowUtc = now,
            PostCount = postCount,
            Reputation = ReputationCalculator.Compute(user.CreatedAtUtc, postCount, now),
            CanPostImages = MemberPerks.CanPostImages(user.IsApproved, user.ApprovedAtUtc, now),
            ImageUnlockUtc = MemberPerks.ImagePostingUnlockUtc(user.IsApproved, user.ApprovedAtUtc),
        });
    }

    // ---- Register -------------------------------------------------------

    private void SetCaptcha()
    {
        var a = Random.Shared.Next(1, 9);
        var b = Random.Shared.Next(1, 9);
        this.HttpContext.Session.SetInt32("captcha", a + b);
        this.ViewBag.Captcha = $"{a} + {b}";
    }

    [HttpGet("register")]
    public IActionResult Register()
    {
        this.SetCaptcha();
        return this.View(new RegisterViewModel());
    }

    [HttpPost("register")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        // Spam-filter: the answer must match the sum stored in session at GET time.
        var expected = this.HttpContext.Session.GetInt32("captcha");
        if (expected is null || !int.TryParse((model.CaptchaAnswer ?? string.Empty).Trim(), out var ans) || ans != expected.Value)
        {
            this.ModelState.AddModelError(nameof(model.CaptchaAnswer), "Incorrect — please solve the sum shown.");
        }

        if (!this.ModelState.IsValid)
        {
            this.SetCaptcha();
            return this.View(model);
        }

        var normalized = model.Username.Trim().ToLowerInvariant();
        if (await this.users.FindByNameAsync(normalized) is not null)
        {
            this.ModelState.AddModelError(nameof(model.Username), "That username is taken.");
            this.SetCaptcha();
            return this.View(model);
        }

        var now = DateTime.UtcNow;
        var user = new AppUser
        {
            UserName = normalized,
            DisplayName = model.Username.Trim(),
            Email = string.IsNullOrWhiteSpace(model.Email) ? null : model.Email.Trim(),
            JoinNote = model.JoinNote.Trim(),
            IsApproved = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        var result = await this.users.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors)
            {
                this.ModelState.AddModelError(string.Empty, e.Description);
            }

            this.SetCaptcha();
            return this.View(model);
        }

        // E2EE keys are generated in the browser (e2ee.js) and posted as hidden fields.
        // They are opaque here — the private keys arrive already wrapped under the password.
        if (!string.IsNullOrWhiteSpace(model.EncPublicKey) && !string.IsNullOrWhiteSpace(model.WrappedPrivateKeys))
        {
            user.EncPublicKey = model.EncPublicKey;
            user.SignPublicKey = model.SignPublicKey;
            user.KdfSalt = model.KdfSalt;
            user.WrappedPrivateKeys = model.WrappedPrivateKeys;
            user.RecoverySalt = model.RecoverySalt;
            user.RecoveryWrappedPrivateKeys = model.RecoveryWrappedPrivateKeys;
            user.E2eeEnabledUtc = now;
            await this.users.UpdateAsync(user);
        }

        await this.users.AddToRoleAsync(user, Roles.Member);
        await this.signIn.SignInAsync(user, isPersistent: true);
        return this.RedirectToAction(nameof(this.Pending));
    }

    private bool IsFetch() =>
        string.Equals(this.Request.Headers["X-Requested-With"], "fetch", StringComparison.OrdinalIgnoreCase);

    [HttpGet("pending")]
    public IActionResult Pending() => this.View();

    // ---- Login / logout -------------------------------------------------

    [HttpGet("login")]
    public IActionResult Login(string? returnUrl = null)
        => this.View(new LoginViewModel { ReturnUrl = returnUrl });

    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!this.ModelState.IsValid)
        {
            return this.View(model);
        }

        var normalized = model.Username.Trim().ToLowerInvariant();
        var user = await this.users.FindByNameAsync(normalized);
        if (user is null || user.IsDeleted)
        {
            this.ModelState.AddModelError(string.Empty, "Invalid username or password.");
            return this.IsFetch()
                ? this.BadRequest(new { ok = false, error = "Invalid username or password." })
                : this.View(model);
        }

        var result = await this.signIn.PasswordSignInAsync(user, model.Password, model.RememberMe, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            var msg = result.IsLockedOut
                ? "This account is temporarily locked after too many attempts. Try again later."
                : "Invalid username or password.";
            this.ModelState.AddModelError(string.Empty, msg);
            return this.IsFetch() ? this.BadRequest(new { ok = false, error = msg }) : this.View(model);
        }

        user.LastLoginAtUtc = DateTime.UtcNow;
        await this.users.UpdateAsync(user);

        var redirect = !string.IsNullOrEmpty(model.ReturnUrl) && this.Url.IsLocalUrl(model.ReturnUrl)
            ? model.ReturnUrl
            : this.Url.Action("Index", "Home");

        // Fetch path: hand back the wrapped private-key blob so the browser can unlock it
        // with the still-in-memory password. hasKeys=false means this member has no keys
        // yet (e.g. an account created before E2EE) — the client generates them now.
        if (this.IsFetch())
        {
            var hasKeys = !string.IsNullOrEmpty(user.EncPublicKey) && !string.IsNullOrEmpty(user.WrappedPrivateKeys);
            return this.Json(new
            {
                ok = true,
                redirect,
                userId = user.Id,
                hasKeys,
                keys = hasKeys ? new
                {
                    kdfSalt = user.KdfSalt,
                    wrappedPrivateKeys = user.WrappedPrivateKeys,
                    encPublicKey = user.EncPublicKey,
                    signPublicKey = user.SignPublicKey,
                } : null,
            });
        }

        return this.Redirect(redirect!);
    }

    // ---- E2EE key lifecycle --------------------------------------------

    /// <summary>Returns the signed-in member's public keys + wrapped private-key blob so the
    /// browser can unlock on this device. The wrapped blob is useless without the password.</summary>
    [HttpGet("e2ee-keys")]
    [Authorize]
    public async Task<IActionResult> E2eeKeys()
    {
        var user = await this.users.GetUserAsync(this.User);
        if (user is null)
        {
            return this.Challenge();
        }

        var hasKeys = !string.IsNullOrEmpty(user.EncPublicKey) && !string.IsNullOrEmpty(user.WrappedPrivateKeys);
        return this.Json(new
        {
            userId = user.Id,
            hasKeys,
            kdfSalt = user.KdfSalt,
            wrappedPrivateKeys = user.WrappedPrivateKeys,
            encPublicKey = user.EncPublicKey,
            signPublicKey = user.SignPublicKey,
            recoverySalt = user.RecoverySalt,
            recoveryWrappedPrivateKeys = user.RecoveryWrappedPrivateKeys,
        });
    }

    /// <summary>Stores the browser-generated public keys + wrapped private blobs. First-time
    /// only — never overwrites existing keys (that would orphan a member's received messages).</summary>
    [HttpPost("e2ee-setup")]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> E2eeSetup(E2eeSetupModel model)
    {
        var user = await this.users.GetUserAsync(this.User);
        if (user is null)
        {
            return this.Challenge();
        }

        if (!string.IsNullOrEmpty(user.EncPublicKey))
        {
            return this.BadRequest(new { ok = false, error = "Encryption keys are already set up for this account." });
        }

        if (string.IsNullOrWhiteSpace(model.EncPublicKey) || string.IsNullOrWhiteSpace(model.SignPublicKey)
            || string.IsNullOrWhiteSpace(model.KdfSalt) || string.IsNullOrWhiteSpace(model.WrappedPrivateKeys))
        {
            return this.BadRequest(new { ok = false, error = "Incomplete key material." });
        }

        user.EncPublicKey = model.EncPublicKey;
        user.SignPublicKey = model.SignPublicKey;
        user.KdfSalt = model.KdfSalt;
        user.WrappedPrivateKeys = model.WrappedPrivateKeys;
        user.RecoverySalt = model.RecoverySalt;
        user.RecoveryWrappedPrivateKeys = model.RecoveryWrappedPrivateKeys;
        user.E2eeEnabledUtc = DateTime.UtcNow;
        await this.users.UpdateAsync(user);

        return this.Json(new { ok = true });
    }

    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await this.signIn.SignOutAsync();
        return this.RedirectToAction("Index", "Home");
    }

    [HttpGet("denied")]
    public IActionResult Denied() => this.View();

    // ---- Settings -------------------------------------------------------

    [HttpGet("settings")]
    [Authorize]
    public async Task<IActionResult> Settings()
    {
        var user = await this.users.GetUserAsync(this.User);
        if (user is null)
        {
            return this.Challenge();
        }

        this.ViewBag.Email = user.Email;
        this.ViewBag.HasEmail = !string.IsNullOrWhiteSpace(user.Email);
        this.ViewBag.EmailNotificationsEnabled = user.EmailNotificationsEnabled;
        return this.View();
    }

    [HttpPost("email-notifications")]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> SetEmailNotifications(bool enabled)
    {
        var user = await this.users.GetUserAsync(this.User);
        if (user is null)
        {
            return this.Challenge();
        }

        if (enabled && string.IsNullOrWhiteSpace(user.Email))
        {
            this.TempData["Error"] = "Add an email address first, then turn on email notifications.";
            return this.RedirectToAction(nameof(this.Settings));
        }

        user.EmailNotificationsEnabled = enabled;
        user.UpdatedAtUtc = DateTime.UtcNow;
        await this.users.UpdateAsync(user);
        this.TempData["Message"] = enabled ? "Email notifications turned on." : "Email notifications turned off.";
        return this.RedirectToAction(nameof(this.Settings));
    }

    [HttpPost("change-password")]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        var user = await this.users.GetUserAsync(this.User);
        if (user is null)
        {
            return this.Challenge();
        }

        if (!this.ModelState.IsValid)
        {
            return this.View(nameof(this.Settings), model);
        }

        var result = await this.users.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors)
            {
                this.ModelState.AddModelError(string.Empty, e.Description);
            }

            return this.View(nameof(this.Settings), model);
        }

        // E2EE: the browser re-wrapped the private keys under the new password and posted the
        // result as hidden fields. Store them so the member can still read their encrypted
        // messages after the password change (otherwise the keys would be orphaned).
        if (!string.IsNullOrWhiteSpace(model.NewKdfSalt) && !string.IsNullOrWhiteSpace(model.NewWrappedPrivateKeys)
            && !string.IsNullOrEmpty(user.EncPublicKey))
        {
            user.KdfSalt = model.NewKdfSalt;
            user.WrappedPrivateKeys = model.NewWrappedPrivateKeys;
            user.UpdatedAtUtc = DateTime.UtcNow;
            await this.users.UpdateAsync(user);
        }

        await this.signIn.RefreshSignInAsync(user);
        this.TempData["Message"] = "Password changed.";
        return this.RedirectToAction(nameof(this.Settings));
    }

    /// <summary>Updates the password-wrapped private-key blob for the current member (used by the
    /// recovery-code flow to re-key after a password reset). Requires an existing E2EE setup.</summary>
    [HttpPost("e2ee-update-wrap")]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> E2eeUpdateWrap(string? kdfSalt, string? wrappedPrivateKeys)
    {
        var user = await this.users.GetUserAsync(this.User);
        if (user is null)
        {
            return this.Challenge();
        }

        if (string.IsNullOrEmpty(user.EncPublicKey) || string.IsNullOrWhiteSpace(kdfSalt) || string.IsNullOrWhiteSpace(wrappedPrivateKeys))
        {
            return this.BadRequest(new { ok = false, error = "Missing key material." });
        }

        user.KdfSalt = kdfSalt;
        user.WrappedPrivateKeys = wrappedPrivateKeys;
        user.UpdatedAtUtc = DateTime.UtcNow;
        await this.users.UpdateAsync(user);
        return this.Json(new { ok = true });
    }

    /// <summary>Page to restore access to encrypted messages using the one-time recovery code
    /// (after a password reset orphaned the password-wrapped keys). All crypto is client-side.</summary>
    [HttpGet("recover-keys")]
    [Authorize]
    public IActionResult RecoverKeys() => this.View();

    [HttpPost("change-username")]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> ChangeUsername(ChangeUsernameViewModel model)
    {
        var user = await this.users.GetUserAsync(this.User);
        if (user is null)
        {
            return this.Challenge();
        }

        if (!await this.users.CheckPasswordAsync(user, model.CurrentPassword))
        {
            this.TempData["Error"] = "Current password is incorrect.";
            return this.RedirectToAction(nameof(this.Settings));
        }

        var normalized = model.NewUsername.Trim().ToLowerInvariant();
        var existing = await this.users.FindByNameAsync(normalized);
        if (existing is not null && existing.Id != user.Id)
        {
            this.TempData["Error"] = "That username is taken.";
            return this.RedirectToAction(nameof(this.Settings));
        }

        user.DisplayName = model.NewUsername.Trim();
        user.UpdatedAtUtc = DateTime.UtcNow;
        var setName = await this.users.SetUserNameAsync(user, normalized);
        if (!setName.Succeeded)
        {
            this.TempData["Error"] = string.Join(" ", setName.Errors.Select(e => e.Description));
            return this.RedirectToAction(nameof(this.Settings));
        }

        await this.users.UpdateAsync(user);
        await this.signIn.RefreshSignInAsync(user);
        this.TempData["Message"] = "Username changed.";
        return this.RedirectToAction(nameof(this.Settings));
    }

    [HttpPost("set-email")]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> SetEmail(SetEmailViewModel model)
    {
        var user = await this.users.GetUserAsync(this.User);
        if (user is null)
        {
            return this.Challenge();
        }

        await this.users.SetEmailAsync(user, string.IsNullOrWhiteSpace(model.Email) ? null : model.Email.Trim());
        this.TempData["Message"] = "Email updated.";
        return this.RedirectToAction(nameof(this.Settings));
    }

    // Dedicated confirmation page — reached via a link on Settings, so deleting an account is a
    // deliberate two-step action rather than a button someone can hit in a moment of frustration.
    [HttpGet("delete")]
    [Authorize]
    public IActionResult DeleteAccount() => this.View(new DeleteAccountViewModel());

    [HttpPost("delete")]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> DeleteAccount(DeleteAccountViewModel model)
    {
        var user = await this.users.GetUserAsync(this.User);
        if (user is null)
        {
            return this.Challenge();
        }

        if (!model.Confirm || !await this.users.CheckPasswordAsync(user, model.CurrentPassword))
        {
            this.TempData["Error"] = "Confirm the box and enter your current password to delete your account.";
            return this.RedirectToAction(nameof(this.DeleteAccount));
        }

        // Soft-delete: keeps threads intact but hides the member and all their posts.
        user.IsDeleted = true;
        user.UpdatedAtUtc = DateTime.UtcNow;
        await this.users.UpdateAsync(user);
        await this.signIn.SignOutAsync();
        this.TempData["Message"] = "Your account was deleted and your posts hidden.";
        return this.RedirectToAction("Index", "Home");
    }

    // ---- Password reset (only usable if the account has an email) --------

    [HttpGet("forgot-password")]
    public IActionResult ForgotPassword() => this.View(new ForgotPasswordViewModel());

    [HttpPost("forgot-password")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!this.ModelState.IsValid)
        {
            return this.View(model);
        }

        var user = await this.users.FindByNameAsync(model.Username.Trim().ToLowerInvariant());
        if (user is not null && !user.IsDeleted && !string.IsNullOrWhiteSpace(user.Email) && this.email.IsConfigured)
        {
            var token = await this.users.GeneratePasswordResetTokenAsync(user);
            var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var link = this.Url.Action(nameof(this.ResetPassword), "Account",
                new { userId = user.Id, token = encoded }, this.Request.Scheme);

            await this.email.SendAsync(
                user.Email!,
                "Reset your Monero User Forum password",
                $"Reset your password using this link:\n\n{link}\n\nIf you didn't request this, ignore this email.");
        }

        // Always show the same message (don't reveal whether an account/email exists).
        this.ViewBag.Sent = true;
        return this.View(model);
    }

    [HttpGet("reset-password")]
    public IActionResult ResetPassword(string userId, string token)
        => this.View(new ResetPasswordViewModel { UserId = userId, Token = token });

    [HttpPost("reset-password")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!this.ModelState.IsValid)
        {
            return this.View(model);
        }

        var user = await this.users.FindByIdAsync(model.UserId);
        if (user is null)
        {
            this.TempData["Message"] = "Password reset. You can now sign in.";
            return this.RedirectToAction(nameof(this.Login));
        }

        var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(model.Token));
        var result = await this.users.ResetPasswordAsync(user, token, model.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors)
            {
                this.ModelState.AddModelError(string.Empty, e.Description);
            }

            return this.View(model);
        }

        this.TempData["Message"] = "Password reset. You can now sign in.";
        return this.RedirectToAction(nameof(this.Login));
    }
}
