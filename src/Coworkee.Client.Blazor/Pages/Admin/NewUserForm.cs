using System.ComponentModel.DataAnnotations;
using Coworkee.Contracts.Identity;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>A new user: either invited by mail to choose a password, or with a password the administrator hands over.</summary>
public sealed class NewUserForm
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public bool IsActive { get; set; } = true;

    public IEnumerable<Guid> Roles { get; set; } = [];

    public bool SendInvitation { get; set; } = true;

    public string? Password { get; set; }

    public bool MustChangePassword { get; set; } = true;

    public CreateUserRequest ToRequest() => SendInvitation
        ? new(Email.Trim(), null, FirstName, LastName, false, [.. Roles], IsActive)
        : new(Email.Trim(), Password ?? string.Empty, FirstName, LastName, MustChangePassword, [.. Roles], IsActive);
}
