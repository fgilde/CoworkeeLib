using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.OData;

namespace Coworkee.Identity.OData;

internal sealed class RoleODataFilter(ICurrentUser currentUser) : IODataEntityFilter<Role>
{
    public Task<IQueryable<Role>> ApplyAsync(IQueryable<Role> query, CancellationToken cancellationToken) =>
        Task.FromResult(query.Where(r => r.TenantId == null || r.TenantId == currentUser.TenantId));
}
