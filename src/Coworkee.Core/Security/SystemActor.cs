namespace Coworkee.Core.Security;

/// <summary>The system acting on its own: signed in without a user, with exactly <see cref="Permissions"/> (as a service client named <see cref="Name"/>).</summary>
public sealed record SystemActor(Guid? TenantId, IReadOnlyCollection<string> Permissions) : ICurrentUser
{
    public const string Name = "coworkee-system";

    public Guid? UserId => null;

    public bool IsAuthenticated => true;

    public IReadOnlyCollection<string> Roles => [];

    public string ClientId => Name;

    public IReadOnlyCollection<string> ClientPermissions => Permissions;
}
