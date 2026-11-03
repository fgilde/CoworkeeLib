namespace Coworkee.Contracts.Identity;

public sealed record ClientRequest(
    string ClientId, string? DisplayName, string ClientType, string ConsentType, IReadOnlyList<string> RedirectUris,
    IReadOnlyList<string> PostLogoutRedirectUris, IReadOnlyList<string> GrantTypes, IReadOnlyList<string> Scopes,
    IReadOnlyList<Guid>? Roles = null, IReadOnlyList<string>? Permissions = null, string? ClientUri = null, string? LogoUrl = null,
    string? Description = null, bool ShowInLauncher = false);
