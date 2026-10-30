namespace Coworkee.Core.Security;

public interface ICurrentUser
{
    Guid? UserId { get; }

    Guid? TenantId { get; }

    bool IsAuthenticated { get; }

    IReadOnlyCollection<string> Roles { get; }

    /// <summary>The service client calling on its own (client credentials, no user), otherwise null.</summary>
    string? ClientId => null;

    /// <summary>Permissions the token itself grants the service client; its <see cref="Roles"/> add theirs.</summary>
    IReadOnlyCollection<string> ClientPermissions => [];

    /// <summary>The API key the request signed in with, otherwise null.</summary>
    Guid? ApiKeyId => null;

    /// <summary>The permissions the credential is limited to (an API key's scopes); null when the user's permissions apply in full.</summary>
    IReadOnlyCollection<string>? PermissionScopes => null;

    /// <summary>The user who signed in as this one ("Log in as"), otherwise null.</summary>
    Guid? ImpersonatorId => null;

    /// <summary>A user's own sign-in in an app (no API key, no service client), as opposed to keys, clients and jobs: the group selection applies to it.</summary>
    bool IsInteractive => false;
}
