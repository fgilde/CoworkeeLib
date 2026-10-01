using Coworkee.Core.Security;

namespace Coworkee.Testing;

public sealed class TestCurrentUser : ICurrentUser
{
    public Guid? UserId { get; set; } = Guid.CreateVersion7();

    public Guid? TenantId { get; set; } = Guid.CreateVersion7();

    public bool IsAuthenticated => UserId is not null;

    public IReadOnlyCollection<string> Roles { get; set; } = [];
}
