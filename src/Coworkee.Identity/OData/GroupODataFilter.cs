using Coworkee.Identity.Domain;
using Coworkee.Identity.Users;
using Coworkee.OData;

namespace Coworkee.Identity.OData;

internal sealed class GroupODataFilter(AdminReach reach) : IODataEntityFilter<UserGroup>
{
    public Task<IQueryable<UserGroup>> ApplyAsync(IQueryable<UserGroup> query, CancellationToken cancellationToken) => reach.FilterGroupsAsync(query, cancellationToken);
}
