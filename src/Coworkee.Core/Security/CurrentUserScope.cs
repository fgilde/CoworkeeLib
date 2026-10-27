namespace Coworkee.Core.Security;

public static class CurrentUserScope
{
    private static readonly AsyncLocal<ICurrentUser?> Ambient = new();

    public static ICurrentUser? Current => Ambient.Value;

    public static IDisposable Begin(ICurrentUser user)
    {
        var previous = Ambient.Value;
        Ambient.Value = user;
        return new Restore(previous);
    }

    /// <summary>
    /// Acts as the system in <paramref name="tenantId"/> with exactly <paramref name="permissions"/> until disposed: signed in, no user, no roles.
    /// Audit entries show "system". Prefer <c>ISystemDispatcher</c>, which also gives the work a scope of its own.
    /// </summary>
    public static IDisposable BeginSystem(Guid? tenantId, params string[] permissions) => Begin(new SystemActor(tenantId, permissions));

    private sealed class Restore(ICurrentUser? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}

/// <summary>Acts as <paramref name="UserId"/>; <paramref name="ImpersonatorId"/> carries who signed in as him ("Log in as") into jobs, the outbox and integration events.</summary>
public sealed record ImpersonatedUser(Guid? UserId, Guid? TenantId, Guid? ImpersonatorId = null) : ICurrentUser
{
    public bool IsAuthenticated => UserId is not null;

    public IReadOnlyCollection<string> Roles => [];
}

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
