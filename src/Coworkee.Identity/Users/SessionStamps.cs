using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Permissions;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Identity.Users;

public sealed record UserStamp(string? SecurityStamp, string? KeptSession);

/// <summary>
/// The security stamps the token check compares, in HybridCache (with Redis as second level when configured). A stamp change drops
/// the entry on every instance through <see cref="SessionSignal"/>; the short lifetime covers instances that cannot hear it.
/// </summary>
public sealed class SessionStamps(HybridCache cache, IServiceScopeFactory scopes)
{
    private static readonly HybridCacheEntryOptions Entry = new()
    {
        Expiration = TimeSpan.FromMinutes(1), LocalCacheExpiration = TimeSpan.FromMinutes(1), Flags = Realtime.CoworkeeRedis.Shared,
    };

    public ValueTask<UserStamp> GetAsync(Guid userId, CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(Key(userId), (userId, scopes), static async (state, ct) =>
        {
            using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
            await using var scope = state.scopes.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>().Set<User>().Where(u => u.Id == state.userId)
                .Select(u => new UserStamp(u.SecurityStamp, u.KeptSession)).SingleOrDefaultAsync(ct) ?? new UserStamp(null, null);
        }, Entry, [PermissionCache.Tag], cancellationToken);

    public ValueTask ForgetAsync(Guid userId, CancellationToken cancellationToken) => cache.RemoveAsync(Key(userId), cancellationToken);

    private static string Key(Guid userId) => $"coworkee:stamp:{userId}";
}
