namespace Coworkee.Contracts.Identity;

/// <summary>The generated secret of a confidential client; it is shown this once and stored only as hash.</summary>
public sealed record ClientSecretDto(Guid Id, string? ClientSecret);
