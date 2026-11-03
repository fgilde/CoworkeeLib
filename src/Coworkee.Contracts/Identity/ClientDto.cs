namespace Coworkee.Contracts.Identity;

/// <summary>
/// An OpenID Connect client ("public" or "confidential", consent "implicit" or "explicit"); <paramref name="Managed"/> ones come from the
/// configuration and change only there. Service clients (grant <see cref="ClientGrantTypes.ClientCredentials"/>) get the permissions of
/// <paramref name="Roles"/> and <paramref name="Permissions"/>. Clients with <paramref name="ShowInLauncher"/> and a <paramref name="ClientUri"/> appear as
/// tiles in the app launcher of the auth server.
/// </summary>
public sealed record ClientDto(
    Guid Id, string ClientId, string? DisplayName, string ClientType, string ConsentType, IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> PostLogoutRedirectUris, IReadOnlyList<string> GrantTypes, IReadOnlyList<string> Scopes, bool Managed,
    IReadOnlyList<Guid> Roles, IReadOnlyList<string> Permissions, string? ClientUri = null, string? LogoUrl = null, string? Description = null,
    bool ShowInLauncher = false);
