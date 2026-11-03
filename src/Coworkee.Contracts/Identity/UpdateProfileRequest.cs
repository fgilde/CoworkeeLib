namespace Coworkee.Contracts.Identity;

public sealed record UpdateProfileRequest(string? FirstName, string? LastName, string? PhoneNumber, PostalAddress? Address = null);
