namespace Coworkee.Contracts.Identity;

/// <summary>A user's language; null follows the organisation.</summary>
public sealed record UserLanguageDto(string? Culture);
