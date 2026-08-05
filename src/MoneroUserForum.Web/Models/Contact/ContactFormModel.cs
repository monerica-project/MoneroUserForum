using System.ComponentModel.DataAnnotations;

namespace MoneroUserForum.Web.Models.Contact;

public class ContactFormModel
{
    [StringLength(80)]
    [Display(Name = "Your name (optional)")]
    public string? FromName { get; set; }

    [EmailAddress]
    [StringLength(255)]
    [Display(Name = "Your email (optional — so we can reply)")]
    public string? FromEmail { get; set; }

    [Required]
    [StringLength(5000, MinimumLength = 5)]
    [Display(Name = "Message")]
    public string Message { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Spam check")]
    public string CaptchaAnswer { get; set; } = string.Empty;
}
