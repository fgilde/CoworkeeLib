using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Core.Security;

namespace Coworkee.Identity.Users.Lookups;

internal sealed class GetUserNamesHandler(IUserDirectory users) : IHandler<GetUserNamesQuery, Result<IReadOnlyDictionary<Guid, string>>>
{
    public async Task<Result<IReadOnlyDictionary<Guid, string>>> HandleAsync(GetUserNamesQuery query, CancellationToken cancellationToken) =>
        Result<IReadOnlyDictionary<Guid, string>>.Success(await users.GetDisplayNamesAsync([.. query.Ids.Take(500).Distinct()], cancellationToken));
}
