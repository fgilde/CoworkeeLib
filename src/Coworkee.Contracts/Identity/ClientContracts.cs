namespace Coworkee.Contracts.Identity;

public static class ClientGrantTypes
{
    public const string AuthorizationCode = "authorization_code";
    public const string RefreshToken = "refresh_token";

    public static readonly IReadOnlyList<string> All = [AuthorizationCode, RefreshToken];
}

/// <summary>An OpenID Connect client ("public" or "confidential", consent "implicit" or "explicit"); <paramref name="Managed"/> ones come from the configuration and change only there.</summary>
public sealed record ClientDto(
    Guid Id, string ClientId, string? DisplayName, string ClientType, string ConsentType, IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> PostLogoutRedirectUris, IReadOnlyList<string> GrantTypes, IReadOnlyList<string> Scopes, bool Managed);

public sealed record ClientRequest(
    string ClientId, string? DisplayName, string ClientType, string ConsentType, IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> PostLogoutRedirectUris, IReadOnlyList<string> GrantTypes, IReadOnlyList<string> Scopes);

/// <summary>The generated secret of a confidential client; it is shown this once and stored only as hash.</summary>
public sealed record ClientSecretDto(Guid Id, string? ClientSecret);

/// <summary>A scope clients may request; <paramref name="Resources"/> become the audiences of the access token.</summary>
public sealed record ScopeDto(Guid Id, string Name, string? DisplayName, string? Description, IReadOnlyList<string> Resources, bool BuiltIn = false);

public sealed record ScopeRequest(string Name, string? DisplayName, string? Description, IReadOnlyList<string> Resources);
