using Coworkee.Core.Security;

namespace Coworkee.Testing;

public sealed class TestCurrentUser : ICurrentUser
{
    private Guid? _userId = Guid.CreateVersion7();
    private Guid? _tenantId = Guid.CreateVersion7();

    public Guid? UserId
    {
        get => CurrentUserScope.Current is { } ambient ? ambient.UserId : _userId;
        set => _userId = value;
    }

    public Guid? TenantId
    {
        get => CurrentUserScope.Current is { } ambient ? ambient.TenantId : _tenantId;
        set => _tenantId = value;
    }

    public bool IsAuthenticated => UserId is not null;

    public IReadOnlyCollection<string> Roles { get; set; } = [];
}
