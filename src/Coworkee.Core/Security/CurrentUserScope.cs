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

    /// <summary>
    /// <paramref name="user"/> as he is now, for <see cref="Begin"/> in work that may outlive the request: the request's user reads the request,
    /// which ends.
    /// </summary>
    public static ICurrentUser Capture(ICurrentUser user) => new CapturedUser(user.UserId, user.TenantId, user.IsAuthenticated, [.. user.Roles], user.ClientId,
        [.. user.ClientPermissions], user.ApiKeyId, user.PermissionScopes is { } scopes ? [.. scopes] : null, user.ImpersonatorId, user.IsInteractive);

    private sealed record CapturedUser(
        Guid? UserId, Guid? TenantId, bool IsAuthenticated, IReadOnlyCollection<string> Roles, string? ClientId, IReadOnlyCollection<string> ClientPermissions,
        Guid? ApiKeyId, IReadOnlyCollection<string>? PermissionScopes, Guid? ImpersonatorId, bool IsInteractive) : ICurrentUser;

    private sealed class Restore(ICurrentUser? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}
