using Coworkee.Contracts.Identity;

namespace Coworkee.Client.Blazor.Pages;

/// <summary>The editable part of the own profile, shown with an object edit form.</summary>
public sealed class ProfileForm
{
    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? PhoneNumber { get; set; }

    public string? Street { get; set; }

    public string? ZipCode { get; set; }

    public string? City { get; set; }

    public string? Country { get; set; }

    public static ProfileForm From(ProfileDto profile) => new()
    {
        FirstName = profile.FirstName,
        LastName = profile.LastName,
        PhoneNumber = profile.PhoneNumber,
        Street = profile.Address?.Street,
        ZipCode = profile.Address?.ZipCode,
        City = profile.Address?.City,
        Country = profile.Address?.Country,
    };

    public UpdateProfileRequest ToRequest() => new(FirstName, LastName, PhoneNumber, new PostalAddress(Street, ZipCode, City, Country));
}
