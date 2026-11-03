namespace Coworkee.Contracts.Identity;

/// <summary>What everyone may change about themselves; the email and the password belong to the sign-in.</summary>
public sealed record ProfileDto(string Email, string? FirstName, string? LastName, string? PhoneNumber, string? AvatarUrl = null, PostalAddress? Address = null, bool HasPassword = true, int PasskeyCount = 0);
