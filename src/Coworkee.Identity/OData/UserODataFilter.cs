using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Users;
using Coworkee.OData;

namespace Coworkee.Identity.OData;

// the own organisation, and with group-scoped administration the users of the caller's reach (AdminReach); search, exports and AI tools read through it
internal sealed class UserODataFilter(ICurrentUser currentUser, AdminReach reach) : IODataEntityFilter<User>
{
    public Task<IQueryable<User>> ApplyAsync(IQueryable<User> query, CancellationToken cancellationToken) =>
        reach.FilterUsersAsync(query.Where(u => u.TenantId == currentUser.TenantId), cancellationToken);
}
