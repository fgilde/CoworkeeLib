using System.ComponentModel.DataAnnotations;
using Coworkee.Contracts.Identity;

namespace Coworkee.Client.Blazor.Pages;

/// <summary>The own new address; the current password confirms that the owner asks.</summary>
public sealed class ChangeEmailForm
{
    [Required]
    [EmailAddress]
    public string NewEmail { get; set; } = string.Empty;

    public string? CurrentPassword { get; set; }

    public ChangeEmailRequest ToRequest() => new(NewEmail.Trim(), CurrentPassword);
}
