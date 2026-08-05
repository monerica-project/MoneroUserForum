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
        var a = Random.Shared.Next(1, 9);
        var b = Random.Shared.Next(1, 9);
        this.HttpContext.Session.SetInt32(CaptchaKey, a + b);
        this.ViewBag.Captcha = $"{a} + {b}";
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
        if (expected is null || !int.TryParse((model.CaptchaAnswer ?? string.Empty).Trim(), out var ans) || ans != expected.Value)
        {
            this.ModelState.AddModelError(nameof(model.CaptchaAnswer), "Incorrect — please solve the sum shown.");
        }

        if (!this.ModelState.IsValid)
        {
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
