using System.ComponentModel.DataAnnotations;
using Coworkee.Contracts.Identity;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>A password an administrator sets for a user.</summary>
public sealed class UserPasswordForm
{
    [Required]
    public string Password { get; set; } = string.Empty;

    public bool MustChangePassword { get; set; } = true;

    public SetPasswordRequest ToRequest() => new(Password, MustChangePassword);
}
