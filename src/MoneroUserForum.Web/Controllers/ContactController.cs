using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using MoneroUserForum.Data;
using MoneroUserForum.Data.Entities;
using MoneroUserForum.Services.Email;
using MoneroUserForum.Web.Models.Contact;

namespace MoneroUserForum.Web.Controllers;

/// <summary>Public contact form → lands in the admin inbox (and is forwarded to admin@).</summary>
[Route("contact")]
public class ContactController : Controller
{
    public const string AdminEmail = "admin@monerouserforum.com";
    private const string CaptchaKey = "contact_captcha";

    private readonly AppDbContext db;
    private readonly IEmailSender email;

    public ContactController(AppDbContext db, IEmailSender email)
    {
        this.db = db;
        this.email = email;
    }

    private void SetCaptcha()
    {
        var (question, answer) = MoneroUserForum.Web.Support.SignupGuard.NewQuestion();
        this.HttpContext.Session.SetInt32(CaptchaKey, answer);
        this.HttpContext.Session.SetString(CaptchaKey + "_at", DateTime.UtcNow.Ticks.ToString());
        this.ViewBag.Captcha = question;
        this.ViewBag.AdminEmail = AdminEmail;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        this.SetCaptcha();
        return this.View(new ContactFormModel());
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(ContactFormModel model)
    {
        var expected = this.HttpContext.Session.GetInt32(CaptchaKey);
        // Answers that arrive faster than a person can read the form are treated as wrong.
        var issuedAt = long.TryParse(this.HttpContext.Session.GetString(CaptchaKey + "_at"), out var ticks) ? new DateTime(ticks, DateTimeKind.Utc) : DateTime.UtcNow;
        var tooFast = DateTime.UtcNow - issuedAt < MoneroUserForum.Web.Support.SignupGuard.MinimumFillTime;
        if (tooFast || expected is null || !int.TryParse((model.CaptchaAnswer ?? string.Empty).Trim(), out var ans) || ans != expected.Value)
        {
            this.ModelState.AddModelError(nameof(model.CaptchaAnswer), "Incorrect — please solve the sum shown.");
        }

        if (!this.ModelState.IsValid)
        {
            this.SetCaptcha();
            return this.View(model);
        }

        var guard = this.HttpContext.RequestServices.GetRequiredService<MoneroUserForum.Web.Support.SignupGuard>();
        if (MoneroUserForum.Web.Support.SignupGuard.LooksAutomated(model.Message)
            || MoneroUserForum.Web.Support.SignupGuard.LooksAutomated(model.FromName)
            || MoneroUserForum.Web.Support.SignupGuard.IsThrowawayTestEmail(model.FromEmail)
            || !guard.TryCount("contact", MoneroUserForum.Web.Support.SignupGuard.ContactMessagesPerHour, MoneroUserForum.Web.Support.SignupGuard.VisitorKey(this.HttpContext), MoneroUserForum.Web.Support.SignupGuard.ContactMessagesPerVisitorPerHour))
        {
            this.ModelState.AddModelError(string.Empty, "Your message could not be sent. Please rewrite it in your own words and try again a little later.");
            this.SetCaptcha();
            return this.View(model);
        }

        // One-word messages and repeats of a message already received today are not stored.
        var text = model.Message.Trim();
        var dayAgo = DateTime.UtcNow.AddDays(-1);
        if (text.Length < 15 || await this.db.ContactMessages.AnyAsync(m => m.Message == text && m.CreatedAtUtc > dayAgo))
        {
            this.ModelState.AddModelError(nameof(model.Message), "Please write a full message (a sentence or more) that you have not already sent.");
            this.SetCaptcha();
            return this.View(model);
        }

        var message = new ContactMessage
        {
            FromName = string.IsNullOrWhiteSpace(model.FromName) ? null : model.FromName.Trim(),
            FromEmail = string.IsNullOrWhiteSpace(model.FromEmail) ? null : model.FromEmail.Trim(),
            Message = model.Message.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
        };
        this.db.ContactMessages.Add(message);
        await this.db.SaveChangesAsync();

        // Best-effort forward to the admin mailbox so it's noticed even without logging in.
        if (this.email.IsConfigured)
        {
            try
            {
                var from = message.FromName ?? "(anonymous)";
                var reply = message.FromEmail is null ? "(none provided)" : message.FromEmail;
                await this.email.SendAsync(
                    AdminEmail,
                    "New contact message — Monero User Forum",
                    $"From: {from}\nReply-to: {reply}\n\n{message.Message}\n\n— Read it in the admin inbox: {this.Request.Scheme}://{this.Request.Host}/admin/messages");
            }
            catch
            {
                // Non-fatal: the message is already saved in the in-site inbox.
            }
        }

        this.ViewBag.Sent = true;
        this.SetCaptcha();
        return this.View(new ContactFormModel());
    }
}
