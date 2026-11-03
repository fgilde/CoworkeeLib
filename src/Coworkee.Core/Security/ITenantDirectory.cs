namespace Coworkee.Core.Security;

public interface ITenantDirectory
{
    Task<bool> IsSystemTenantAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<Guid?> GetSystemTenantIdAsync(CancellationToken cancellationToken);

    Task<string?> GetNameAsync(Guid tenantId, CancellationToken cancellationToken) => Task.FromResult<string?>(null);

    /// <summary>Every active organisation, for work that runs in each of them.</summary>
    Task<IReadOnlyList<Guid>> GetActiveTenantIdsAsync(CancellationToken cancellationToken) => throw new NotSupportedException($"{GetType().Name} cannot list the organisations.");
}
