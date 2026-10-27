using Coworkee.Core.Security;

namespace Coworkee.Testing;

public sealed class TestCurrentUser : ICurrentUser
{
    private Guid? _userId = Guid.CreateVersion7();
    private Guid? _tenantId = Guid.CreateVersion7();
    private IReadOnlyCollection<string> _roles = [];

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

    public bool IsAuthenticated => CurrentUserScope.Current?.IsAuthenticated ?? UserId is not null;

    public IReadOnlyCollection<string> Roles
    {
        get => CurrentUserScope.Current?.Roles ?? _roles;
        set => _roles = value;
    }

    public string? ClientId => CurrentUserScope.Current?.ClientId;

    public IReadOnlyCollection<string> ClientPermissions => CurrentUserScope.Current?.ClientPermissions ?? [];

    private Guid? _impersonatorId;

    public Guid? ImpersonatorId
    {
        get => CurrentUserScope.Current is { } ambient ? ambient.ImpersonatorId : _impersonatorId;
        set => _impersonatorId = value;
    }
}
