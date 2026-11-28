using Coworkee.Application.Messaging;
using Coworkee.Core.Results;

namespace Coworkee.Identity.Groups;

internal sealed class GetLockedGroupsHandler(Users.AdminReach reach) : IHandler<GetLockedGroups, Result<IReadOnlyList<Guid>>>
{
    public async Task<Result<IReadOnlyList<Guid>>> HandleAsync(GetLockedGroups query, CancellationToken cancellationToken) =>
        (await reach.RootsAsync(cancellationToken))?.Select(r => r.Id).ToList() ?? [];
}
