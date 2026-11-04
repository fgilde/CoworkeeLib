using System.Data.Common;
using Coworkee.Application.Authorization;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coworkee.Identity.Permissions;

/// <summary>The custom permissions as definitions; cached under the permission tag, so every change drops them on every instance.</summary>
internal sealed partial class CustomPermissionDefinitions(HybridCache cache, IServiceScopeFactory scopes, ILogger<CustomPermissionDefinitions> logger)
    : IStoredPermissionDefinitions
{
    internal const string Key = "coworkee:permissions:custom";

    public IReadOnlyList<PermissionDefinition> Current
    {
        get
        {
            try
            {
                var pending = cache.GetOrCreateAsync(Key, LoadAsync, tags: [PermissionCache.Tag]);
                // shortcut: blocks while the cache loads, after a change only, as IPermissionDefinitionManager is synchronous; an async manager when that shows
                return (pending.IsCompletedSuccessfully ? pending.Result : pending.AsTask().GetAwaiter().GetResult()).Definitions;
            }
            catch (DbException exception)
            {
                // an app that did not add the migration yet keeps its permissions in code working
                LogLoadFailed(exception);
                return [];
            }
        }
    }

    private async ValueTask<CustomPermissionSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var permissions = await scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>().Set<CustomPermission>().AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new { p.Name, p.Group, p.DisplayName })
            .ToListAsync(cancellationToken);
        return new CustomPermissionSnapshot([.. permissions.Select(p => new PermissionDefinition(p.Name, p.Group, p.DisplayName, []))]);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Loading the custom permissions failed; only the permissions in code are defined")]
    private partial void LogLoadFailed(Exception exception);
}
