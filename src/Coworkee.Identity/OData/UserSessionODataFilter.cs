using Coworkee.Identity.Domain;
using Coworkee.Identity.Sessions;
using Coworkee.Identity.Users;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData;

namespace Coworkee.Identity.OData;

// the sessions of users in reach, like their API keys
internal sealed class UserSessionODataFilter(CoworkeeDbContext db, AdminReach reach) : IODataEntityFilter<UserSession>
{
    public async Task<IQueryable<UserSession>> ApplyAsync(IQueryable<UserSession> query, CancellationToken cancellationToken)
    {
        if (await reach.UserFilterAsync(cancellationToken) is not { } within)
        {
            return query;
        }

        var users = db.Set<User>().Where(within);
        return query.Where(s => users.Any(u => u.Id == s.UserId));
    }
}
