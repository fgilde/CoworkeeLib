using System.ComponentModel.DataAnnotations;
using Coworkee.Contracts.Identity;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>What an administrator edits of a user on the user page; the address and the password have their own dialogs.</summary>
public sealed class UserForm
{
    [Required]
    public string UserName { get; set; } = string.Empty;

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    /// <summary>Empty follows the organisation.</summary>
    public string Language { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    public string? Street { get; set; }

    public string? ZipCode { get; set; }

    public string? City { get; set; }

    public string? Country { get; set; }

    public bool IsActive { get; set; }

    public bool EmailConfirmed { get; set; }

    public bool MustChangePassword { get; set; }

    public static UserForm From(UserDetailDto user, string? language) => new()
    {
        UserName = user.UserName,
        FirstName = user.FirstName,
        LastName = user.LastName,
        Language = language ?? string.Empty,
        PhoneNumber = user.PhoneNumber,
        Street = user.Address?.Street,
        ZipCode = user.Address?.ZipCode,
        City = user.Address?.City,
        Country = user.Address?.Country,
        IsActive = user.IsActive,
        EmailConfirmed = user.EmailConfirmed,
        MustChangePassword = user.MustChangePassword,
    };

    public UpdateUserRequest ToRequest() => new(
        FirstName, LastName, IsActive, MustChangePassword, UserName, PhoneNumber ?? string.Empty,
        new PostalAddress(Street, ZipCode, City, Country), EmailConfirmed);
}
