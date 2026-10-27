using Coworkee.Identity.Domain;
using Coworkee.OData;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.OData;

/// <summary>Loads the roles of the groups, so the Groups set shows them instead of an empty list.</summary>
internal sealed class GroupODataFilter : IODataEntityFilter<UserGroup>
{
    public Task<IQueryable<UserGroup>> ApplyAsync(IQueryable<UserGroup> query, CancellationToken cancellationToken) => Task.FromResult(query.Include(g => g.Roles).AsQueryable());
}
