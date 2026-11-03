using Coworkee.Identity.Domain;
using Coworkee.Identity.Users;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData;

namespace Coworkee.Identity.OData;

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
