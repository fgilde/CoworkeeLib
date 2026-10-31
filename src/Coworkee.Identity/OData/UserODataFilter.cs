using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Users;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Identity.OData;

// the own organisation, and with group-scoped administration the users of the caller's reach (AdminReach); search, exports and AI tools read through it
internal sealed class UserODataFilter(ICurrentUser currentUser, AdminReach reach) : IODataEntityFilter<User>
{
    public Task<IQueryable<User>> ApplyAsync(IQueryable<User> query, CancellationToken cancellationToken) =>
        reach.FilterUsersAsync(query.Where(u => u.TenantId == currentUser.TenantId), cancellationToken);
}

internal sealed class GroupODataFilter(AdminReach reach) : IODataEntityFilter<UserGroup>
{
    public Task<IQueryable<UserGroup>> ApplyAsync(IQueryable<UserGroup> query, CancellationToken cancellationToken) => reach.FilterGroupsAsync(query, cancellationToken);
}

// someone else's key is his account: the keys of users in reach
internal sealed class ApiKeyODataFilter(CoworkeeDbContext db, AdminReach reach) : IODataEntityFilter<ApiKeys.ApiKey>
{
    public async Task<IQueryable<ApiKeys.ApiKey>> ApplyAsync(IQueryable<ApiKeys.ApiKey> query, CancellationToken cancellationToken)
    {
        if (await reach.UserFilterAsync(cancellationToken) is not { } within)
        {
            return query;
        }

        var users = db.Set<User>().Where(within);
        return query.Where(k => users.Any(u => u.Id == k.UserId));
    }
}
