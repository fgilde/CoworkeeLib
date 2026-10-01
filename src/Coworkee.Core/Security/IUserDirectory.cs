namespace Coworkee.Core.Security;

public interface IUserDirectory
{
    Task<string?> GetEmailAsync(Guid userId, CancellationToken cancellationToken);
}

public interface ITenantDirectory
{
    Task<bool> IsSystemTenantAsync(Guid tenantId, CancellationToken cancellationToken);
}
