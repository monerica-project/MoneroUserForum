using System.ComponentModel.DataAnnotations;

namespace MoneroUserForum.Web.Models.Account;

public class RegisterViewModel
{
    [Required]
    [StringLength(20, MinimumLength = 4)]
    [RegularExpression("^[a-zA-Z0-9_-]+$", ErrorMessage = "Username can only contain letters, numbers, dashes, and underscores.")]
    [Display(Name = "Username")]
    public string Username { get; set; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 8)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "Confirm password")]
    [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [EmailAddress]
    [Display(Name = "Email (optional — enables password reset)")]
    public string? Email { get; set; }

    [Required]
    [StringLength(1000, MinimumLength = 10)]
    [Display(Name = "Message to the admin (why you'd like to join)")]
    public string JoinNote { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Spam check")]
    public string CaptchaAnswer { get; set; } = string.Empty;
}

public class LoginViewModel
{
    [Required]
    [Display(Name = "Username")]
    public string Username { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Keep me signed in")]
    public bool RememberMe { get; set; } = true;

    public string? ReturnUrl { get; set; }
}

public class ChangePasswordViewModel
{
    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 8)]
    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "Confirm new password")]
    [Compare(nameof(NewPassword), ErrorMessage = "The passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class ChangeUsernameViewModel
{
    [Required]
    [StringLength(20, MinimumLength = 4)]
    [RegularExpression("^[a-zA-Z0-9_-]+$", ErrorMessage = "Username can only contain letters, numbers, dashes, and underscores.")]
    [Display(Name = "New username")]
    public string NewUsername { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = string.Empty;
}

public class SetEmailViewModel
{
    [EmailAddress]
    [Display(Name = "Email (leave blank to remove)")]
    public string? Email { get; set; }
}

public class ForgotPasswordViewModel
{
    [Required]
    [Display(Name = "Username")]
    public string Username { get; set; } = string.Empty;
}

public class ResetPasswordViewModel
{
    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    public string Token { get; set; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 8)]
    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "Confirm new password")]
    [Compare(nameof(NewPassword), ErrorMessage = "The passwords do not match.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class DeleteAccountViewModel
{
    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Display(Name = "Yes, permanently delete my account and hide my posts")]
    public bool Confirm { get; set; }
}
