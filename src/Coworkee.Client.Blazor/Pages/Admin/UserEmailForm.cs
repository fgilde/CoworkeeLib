using System.ComponentModel.DataAnnotations;
using Coworkee.Contracts.Identity;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>A user's new address; unconfirmed it gets a confirmation mail.</summary>
public sealed class UserEmailForm
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    public bool Confirmed { get; set; } = true;

    public SetUserEmailRequest ToRequest() => new(Email.Trim(), Confirmed);
}
