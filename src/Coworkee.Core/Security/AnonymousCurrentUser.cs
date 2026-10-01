namespace Coworkee.Core.Security;

public sealed class AnonymousCurrentUser : ICurrentUser
{
    public Guid? UserId => null;

    public Guid? TenantId => null;

    public bool IsAuthenticated => false;

    public IReadOnlyCollection<string> Roles => [];
}
