using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Users;

internal sealed class TenantDirectory(CoworkeeDbContext db) : ITenantDirectory
{
    public Task<bool> IsSystemTenantAsync(Guid tenantId, CancellationToken cancellationToken) =>
        db.Set<Tenant>().AnyAsync(t => t.Id == tenantId && t.IsDefault, cancellationToken);

    public Task<Guid?> GetSystemTenantIdAsync(CancellationToken cancellationToken) =>
        db.Set<Tenant>().Where(t => t.IsDefault).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(cancellationToken);

    public Task<string?> GetNameAsync(Guid tenantId, CancellationToken cancellationToken) =>
        db.Set<Tenant>().Where(t => t.Id == tenantId).Select(t => t.Name).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetActiveTenantIdsAsync(CancellationToken cancellationToken) =>
        await db.Set<Tenant>().Where(t => t.IsActive).OrderBy(t => t.Id).Select(t => t.Id).ToListAsync(cancellationToken);
}
