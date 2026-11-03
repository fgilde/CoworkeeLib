namespace Coworkee.Contracts.Files;

/// <summary>A short-lived anonymous link: <paramref name="Url"/> (relative to the API) opens the file without sign-in until <paramref name="ExpiresAt"/>.</summary>
public sealed record FileTokenDto(string Token, string Url, DateTimeOffset ExpiresAt);
