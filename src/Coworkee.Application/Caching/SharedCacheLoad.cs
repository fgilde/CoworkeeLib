using Coworkee.Core.Security;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Application.Caching;

public static class SharedCacheLoad
{
    /// <summary>
    /// <see cref="HybridCache.GetOrCreateAsync{T}(string, Func{CancellationToken, ValueTask{T}}, HybridCacheEntryOptions?, IEnumerable{string}?, CancellationToken)"/>
    /// for a load that reads scoped services (a DbContext, the dispatcher): every request asking for the key while it loads waits on this one load, so it
    /// must not use the services or the cancellation of the request that happened to start it, which may end (or be aborted) first. The factory gets a
    /// service scope of its own and runs as <paramref name="actor"/>; its token is the cache's shared one, canceled only when every waiting caller is.
    /// Inside an open transaction of the caller (<see cref="IUnitOfWork.InTransaction"/>) the factory runs in the caller's scope and nothing is cached:
    /// another connection would not see what the transaction wrote (or wait on its locks), and it may be rolled back.
    /// </summary>
    /// <param name="services">The caller's services.</param>
    /// <param name="actor">Who the load acts as, e.g. <c>new ImpersonatedUser(null, tenantId)</c>: the load may run on a pool thread without the caller's context.</param>
    public static ValueTask<T> GetOrCreateInScopeAsync<T>(
        this HybridCache cache, IServiceProvider services, ICurrentUser actor, string key, Func<IServiceProvider, CancellationToken, Task<T>> factory,
        HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
    {
        if (services.GetService<IUnitOfWork>() is { InTransaction: true })
        {
            return new ValueTask<T>(InCallerScopeAsync());
        }

        return cache.GetOrCreateAsync(key, (scopes: services.GetRequiredService<IServiceScopeFactory>(), actor, factory), static async (state, ct) =>
        {
            using var acting = CurrentUserScope.Begin(state.actor);
            await using var scope = state.scopes.CreateAsyncScope();
            return await state.factory(scope.ServiceProvider, ct);
        }, options, tags, cancellationToken);

        async Task<T> InCallerScopeAsync()
        {
            using var acting = CurrentUserScope.Begin(actor);
            return await factory(services, cancellationToken);
        }
    }
}
