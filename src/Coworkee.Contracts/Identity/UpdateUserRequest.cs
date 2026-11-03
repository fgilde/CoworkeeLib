namespace Coworkee.Contracts.Identity;

/// <summary>Changes a user; the optional values left null stay as they are, empty texts clear them.</summary>
public sealed record UpdateUserRequest(
    string? FirstName, string? LastName, bool IsActive, bool? MustChangePassword = null,
    string? UserName = null, string? PhoneNumber = null, PostalAddress? Address = null, bool? EmailConfirmed = null);
