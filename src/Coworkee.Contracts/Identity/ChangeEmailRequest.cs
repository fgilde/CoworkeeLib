namespace Coworkee.Contracts.Identity;

/// <summary>The own new address; <paramref name="CurrentPassword"/> is needed when the account has a password.</summary>
public sealed record ChangeEmailRequest(string NewEmail, string? CurrentPassword);
