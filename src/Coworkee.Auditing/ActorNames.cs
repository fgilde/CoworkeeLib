using Coworkee.Core.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Auditing;

internal static class ActorNames
{
    public static async Task<IReadOnlyDictionary<Guid, string>> LoadAsync(IServiceProvider services, IEnumerable<Guid?> ids, CancellationToken cancellationToken)
    {
        var distinct = ids.OfType<Guid>().Distinct().ToList();
        return distinct.Count == 0 || services.GetService<IUserDirectory>() is not { } users
            ? new Dictionary<Guid, string>()
            : await users.GetDisplayNamesAsync(distinct, cancellationToken);
    }
}
