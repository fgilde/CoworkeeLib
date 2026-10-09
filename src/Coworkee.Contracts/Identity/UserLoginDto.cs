namespace Coworkee.Contracts.Identity;

/// <summary>An external sign-in (Google, Microsoft, ...) linked to a user.</summary>
public sealed record UserLoginDto(string LoginProvider, string ProviderKey, string? DisplayName);
