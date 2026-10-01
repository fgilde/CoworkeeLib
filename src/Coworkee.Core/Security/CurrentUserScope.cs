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

    private sealed class Restore(ICurrentUser? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}

public sealed record ImpersonatedUser(Guid? UserId, Guid? TenantId) : ICurrentUser
{
    public bool IsAuthenticated => UserId is not null;

    public IReadOnlyCollection<string> Roles => [];
}
