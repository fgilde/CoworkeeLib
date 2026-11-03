using System.Globalization;
using System.Reflection;
using Coworkee.Application.Groups;
using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coworkee.Application.Caching;

internal sealed class CachingMiddleware(HybridCache cache, ICurrentUser currentUser, IServiceProvider services) : IRequestMiddleware
{
    public int Order => MiddlewareOrder.Caching;

    public async Task<TResult> InvokeAsync<TRequest, TResult>(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
        where TRequest : IRequest<TResult>
    {
        // inside an open transaction the query reads through it and is not cached: it may see writes that are rolled back
        if (request is ICachedQuery query && (query.CacheScope != CacheScope.User || currentUser.UserId is not null) && services.GetService<IUnitOfWork>() is not { InTransaction: true })
        {
            var scopes = services.GetRequiredService<IServiceScopeFactory>();
            // every request asking while it loads waits on this load: it runs the rest of the pipeline in a scope of its own, as the caller, with the
            // cache's token, so the request that started it may end (or be aborted) first (L64)
            var caller = CurrentUserScope.Capture(currentUser);
            return await ReadThroughAsync(query, typeof(TRequest), async ct =>
            {
                using var acting = CurrentUserScope.Begin(caller);
                await using var scope = scopes.CreateAsyncScope();
                return await RequestInvoker<TRequest, TResult>.Pipeline(request, scope.ServiceProvider, ct, GetType())();
            }, cancellationToken);
        }

        var result = await next();
        if (request is IInvalidatesCache invalidation && Succeeded(result))
        {
            // a global entry is shared by every organisation: a command anywhere invalidates it
            await cache.RemoveByTagAsync(invalidation.CacheTags.SelectMany(tag => new[] { Tag(tag), GlobalTag(tag) }).ToList(), cancellationToken);
        }

        return result;
    }

    private Task<TResult> ReadThroughAsync<TResult>(ICachedQuery query, Type requestType, Func<CancellationToken, Task<TResult>> load, CancellationToken cancellationToken)
    {
        if (typeof(TResult).IsGenericType && typeof(TResult).GetGenericTypeDefinition() == typeof(Result<>))
        {
            var read = ReadResultMethods.GetOrAdd(typeof(TResult), type => ReadResultMethod.MakeGenericMethod(type.GetGenericArguments()[0]));
            return (Task<TResult>)read.Invoke(this, [query, requestType, load, cancellationToken])!;
        }

        return ReadAsync(query, requestType, load, cancellationToken);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, MethodInfo> ReadResultMethods = new();

    private static readonly MethodInfo ReadResultMethod =
        typeof(CachingMiddleware).GetMethod(nameof(ReadResultAsync), BindingFlags.Instance | BindingFlags.NonPublic)!;

    // a failure is not cached; it reaches every caller that waited on the load
    private async Task<Result<TValue>> ReadResultAsync<TValue>(
        ICachedQuery query, Type requestType, Func<CancellationToken, Task<Result<TValue>>> load, CancellationToken cancellationToken)
    {
        try
        {
            return Result<TValue>.Success(await ReadAsync(
                query,
                requestType,
                async ct =>
                {
                    var result = await load(ct);
                    return result.IsSuccess ? result.Value : throw new NotCached(result);
                },
                cancellationToken));
        }
        catch (NotCached notCached)
        {
            return (Result<TValue>)notCached.Result;
        }
    }

    private async Task<TValue> ReadAsync<TValue>(ICachedQuery query, Type requestType, Func<CancellationToken, Task<TValue>> load, CancellationToken cancellationToken)
    {
        var defaults = services.GetService<ICacheDefaults>() is { } source ? await source.GetAsync(cancellationToken) : null;
        var options = (query.CacheDuration ?? defaults?.Duration) is { } duration ? new HybridCacheEntryOptions { Expiration = duration, LocalCacheExpiration = duration } : null;
        var key = Key(query, requestType) + (query.CacheByGroups ? $":groups:{await GroupsAsync(cancellationToken)}" : string.Empty);
        var tags = query.CacheTags.Select(tag => query.CacheScope == CacheScope.Global ? GlobalTag(tag) : Tag(tag)).ToList();
        var caller = ExecutionContext.Capture();
        var refreshAfter = defaults is { RefreshInBackground: false } ? null : query.CacheRefreshAfter ?? defaults?.RefreshAfter;
        if (refreshAfter is not { } after)
        {
            return await cache.GetOrCreateAsync(key, async ct => await InCallerContext(caller, () => load(ct)), options, tags, cancellationToken);
        }

        // stale while revalidate: the entry carries its age, under a key of its own (an entry of the plain value may be in the distributed cache)
        key += ":refreshed";
        var entry = await cache.GetOrCreateAsync(
            key, async ct => new CacheEntry<TValue>(await InCallerContext(caller, () => load(ct)), DateTimeOffset.UtcNow), options, tags, cancellationToken);
        if (DateTimeOffset.UtcNow - entry.LoadedUtc >= after)
        {
            Refresh(key, load, options, tags);
        }

        return entry.Value;
    }

    // one refresh per entry on this instance, and with a distributed lock one across the instances; a failed load keeps the old entry
    private void Refresh<TValue>(string key, Func<CancellationToken, Task<TValue>> load, HybridCacheEntryOptions? options, IReadOnlyList<string> tags)
    {
        if (!Refreshing.TryAdd(key, 0))
        {
            return;
        }

        var locks = services.GetService<IDistributedLock>();
        var logger = services.GetService<ILogger<CachingMiddleware>>();
        _ = Task.Run(async () =>
        {
            try
            {
                await using var held = locks is null ? null : await locks.AcquireAsync($"{key}:refresh", TimeSpan.Zero);
                if (locks is null || held is not null)
                {
                    var value = await load(CancellationToken.None);
                    await cache.SetAsync(key, new CacheEntry<TValue>(value, DateTimeOffset.UtcNow), options, tags);
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                logger?.LogDebug(exception, "Refreshing the cache entry {Key} failed; the old entry stays", key);
            }
            finally
            {
                Refreshing.TryRemove(key, out _);
            }
        });
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> Refreshing = new(StringComparer.Ordinal);

    // the active groups as one short part of the key (a selection may hold many groups)
    private async Task<string> GroupsAsync(CancellationToken cancellationToken)
    {
        var groups = services.GetService<IGroupScope>() is { } scope ? await scope.ActiveAsync(cancellationToken) : [];
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join(",", groups.Order()))))[..32];
    }

    // HybridCache may run the factory on a pool thread without the caller's ExecutionContext, which would lose the culture and the ambient values.
    private static Task<TValue> InCallerContext<TValue>(ExecutionContext? caller, Func<Task<TValue>> load)
    {
        if (caller is null)
        {
            return load();
        }

        Task<TValue>? task = null;
        ExecutionContext.Run(caller, _ => task = load(), null);
        return task!;
    }

    // the culture is part of every key: a handler may answer in the caller's language or format
    private string Key(ICachedQuery query, Type requestType) => query.CacheScope switch
    {
        // a key (its scopes) and a "Log in as" session may see less than the user's own session
        CacheScope.User => $"coworkee:query:{currentUser.TenantId}:{currentUser.UserId}:{Credential()}:{requestType.FullName}:{Culture()}:{query.CacheKey}",
        CacheScope.Global => $"coworkee:query:global:{requestType.FullName}:{Culture()}:{query.CacheKey}",
        // a signed-in user without a tenant is not an anonymous caller
        _ => $"coworkee:query:{currentUser.TenantId?.ToString() ?? (currentUser.IsAuthenticated ? "host" : "anonymous")}:{requestType.FullName}:{Culture()}:{query.CacheKey}",
    };

    private static string Culture() => $"{CultureInfo.CurrentCulture.Name}|{CultureInfo.CurrentUICulture.Name}";

    private string Credential() =>
        (currentUser.ApiKeyId is { } key ? $"key-{key}" : "session") + (currentUser.ImpersonatorId is { } impersonator ? $"-as-{impersonator}" : string.Empty);

    private string Tag(string tag) => $"coworkee:{currentUser.TenantId?.ToString() ?? "anonymous"}:{tag}";

    private static string GlobalTag(string tag) => $"coworkee:global:{tag}";

    private static bool Succeeded<TResult>(TResult result) => result is not Result { IsSuccess: false };

    private sealed class NotCached(object result) : Exception
    {
        public object Result { get; } = result;
    }
}
