using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.OData;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.OData;

internal sealed class UserODataFilter(ICurrentUser currentUser) : IODataEntityFilter<User>
{
    public Task<IQueryable<User>> ApplyAsync(IQueryable<User> query, CancellationToken cancellationToken) =>
        // the memberships come along, so Groups shows them instead of an empty list
        Task.FromResult(query.Where(u => u.TenantId == currentUser.TenantId).Include(u => u.Groups).AsQueryable());
}
