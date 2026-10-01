namespace Coworkee.Core.Security;

public interface IUserDirectory
{
    Task<string?> GetEmailAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);
}

public interface ITenantDirectory
{
    Task<bool> IsSystemTenantAsync(Guid tenantId, CancellationToken cancellationToken);
}
