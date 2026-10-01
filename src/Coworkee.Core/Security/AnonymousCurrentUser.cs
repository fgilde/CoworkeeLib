namespace Coworkee.Core.Security;

public sealed class AnonymousCurrentUser : ICurrentUser
{
    public Guid? UserId => CurrentUserScope.Current?.UserId;

    public Guid? TenantId => CurrentUserScope.Current?.TenantId;

    public bool IsAuthenticated => CurrentUserScope.Current?.IsAuthenticated ?? false;

    public IReadOnlyCollection<string> Roles => CurrentUserScope.Current?.Roles ?? [];
}
