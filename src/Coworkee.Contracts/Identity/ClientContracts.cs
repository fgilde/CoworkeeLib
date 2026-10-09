namespace Coworkee.Contracts.Identity;

public static class ClientGrantTypes
{
    public const string AuthorizationCode = "authorization_code";
    public const string RefreshToken = "refresh_token";

    /// <summary>A service client: a confidential client that calls APIs on its own, without a user.</summary>
    public const string ClientCredentials = "client_credentials";

    public static readonly IReadOnlyList<string> All = [AuthorizationCode, RefreshToken, ClientCredentials];
}

/// <summary>
/// An OpenID Connect client ("public" or "confidential", consent "implicit" or "explicit"); <paramref name="Managed"/> ones come from the
/// configuration and change only there. Service clients (grant <see cref="ClientGrantTypes.ClientCredentials"/>) get the permissions of
/// <paramref name="Roles"/> and <paramref name="Permissions"/>.
/// </summary>
public sealed record ClientDto(
    Guid Id, string ClientId, string? DisplayName, string ClientType, string ConsentType, IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> PostLogoutRedirectUris, IReadOnlyList<string> GrantTypes, IReadOnlyList<string> Scopes, bool Managed,
    IReadOnlyList<Guid> Roles, IReadOnlyList<string> Permissions);

public sealed record ClientRequest(
    string ClientId, string? DisplayName, string ClientType, string ConsentType, IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> PostLogoutRedirectUris, IReadOnlyList<string> GrantTypes, IReadOnlyList<string> Scopes,
    IReadOnlyList<Guid>? Roles = null, IReadOnlyList<string>? Permissions = null);

/// <summary>The generated secret of a confidential client; it is shown this once and stored only as hash.</summary>
public sealed record ClientSecretDto(Guid Id, string? ClientSecret);

/// <summary>A scope clients may request; <paramref name="Resources"/> become the audiences of the access token.</summary>
public sealed record ScopeDto(Guid Id, string Name, string? DisplayName, string? Description, IReadOnlyList<string> Resources, bool BuiltIn = false);

public sealed record ScopeRequest(string Name, string? DisplayName, string? Description, IReadOnlyList<string> Resources);
