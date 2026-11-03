namespace Coworkee.Contracts.Theming;

/// <summary>The Keycloak login theme the API keeps in the app's design; <see cref="Configured"/> is false without a theme folder.</summary>
public sealed record KeycloakThemeStatusDto(bool Configured, string? ThemeName, DateTimeOffset? WrittenAt, string? Error);
