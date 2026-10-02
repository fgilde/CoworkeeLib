namespace Coworkee.Core.Security;

public interface IUserDirectory
{
    Task<string?> GetEmailAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    /// <summary>E-mail addresses of those users who are active in an active tenant; others are left out.</summary>
    async Task<IReadOnlyDictionary<Guid, string>> GetActiveEmailsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        var emails = new Dictionary<Guid, string>();
        foreach (var userId in userIds)
        {
            if (await GetEmailAsync(userId, cancellationToken) is { Length: > 0 } email)
            {
                emails[userId] = email;
            }
        }

        return emails;
    }
}

public interface ITenantDirectory
{
    Task<bool> IsSystemTenantAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<Guid?> GetSystemTenantIdAsync(CancellationToken cancellationToken);
}
