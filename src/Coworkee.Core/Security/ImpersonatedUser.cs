namespace Coworkee.Core.Security;

/// <summary>Acts as <paramref name="UserId"/>; <paramref name="ImpersonatorId"/> carries who signed in as him ("Log in as") into jobs, the outbox and integration events.</summary>
public sealed record ImpersonatedUser(Guid? UserId, Guid? TenantId, Guid? ImpersonatorId = null) : ICurrentUser
{
    public bool IsAuthenticated => UserId is not null;

    public IReadOnlyCollection<string> Roles => [];
}
