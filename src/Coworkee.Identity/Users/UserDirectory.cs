using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Users;

internal sealed class UserDirectory(CoworkeeDbContext db) : IUserDirectory
{
    public Task<string?> GetEmailAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Set<User>().Where(u => u.Id == userId).Select(u => u.Email).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        (await db.Set<User>().Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.FirstName, u.LastName, u.Email }).ToListAsync(cancellationToken))
            .ToDictionary(
                u => u.Id,
                u => string.Join(' ', new[] { u.FirstName, u.LastName }.Where(n => !string.IsNullOrWhiteSpace(n))) is { Length: > 0 } name ? name : u.Email ?? u.Id.ToString());

    public async Task<IReadOnlyDictionary<Guid, string>> GetActiveEmailsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        await (from user in db.Set<User>()
               join tenant in db.Set<Tenant>() on user.TenantId equals tenant.Id
               where userIds.Contains(user.Id) && user.IsActive && tenant.IsActive && user.Email != null
               select new { user.Id, user.Email }).ToDictionaryAsync(u => u.Id, u => u.Email!, cancellationToken);
}

internal sealed class TenantDirectory(CoworkeeDbContext db) : ITenantDirectory
{
    public Task<bool> IsSystemTenantAsync(Guid tenantId, CancellationToken cancellationToken) =>
        db.Set<Tenant>().AnyAsync(t => t.Id == tenantId && t.IsDefault, cancellationToken);

    public Task<Guid?> GetSystemTenantIdAsync(CancellationToken cancellationToken) =>
        db.Set<Tenant>().Where(t => t.IsDefault).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(cancellationToken);
}
