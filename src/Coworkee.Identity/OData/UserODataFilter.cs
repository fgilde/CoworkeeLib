using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.OData;

namespace Coworkee.Identity.OData;

internal sealed class UserODataFilter(ICurrentUser currentUser) : IODataEntityFilter<User>
{
    public Task<IQueryable<User>> ApplyAsync(IQueryable<User> query, CancellationToken cancellationToken) =>
        Task.FromResult(query.Where(u => u.TenantId == currentUser.TenantId));
}
