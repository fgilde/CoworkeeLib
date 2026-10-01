using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.Users;

internal sealed class UserDirectory(CoworkeeDbContext db) : IUserDirectory
{
    public Task<string?> GetEmailAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Set<User>().Where(u => u.Id == userId).Select(u => u.Email).SingleOrDefaultAsync(cancellationToken);
}

internal sealed class TenantDirectory(CoworkeeDbContext db) : ITenantDirectory
{
    public Task<bool> IsSystemTenantAsync(Guid tenantId, CancellationToken cancellationToken) =>
        db.Set<Tenant>().AnyAsync(t => t.Id == tenantId && t.IsDefault, cancellationToken);
}
