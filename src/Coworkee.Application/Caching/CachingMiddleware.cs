using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using System.Reflection;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

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
                return await RequestInvoker<TRequest, TResult>.Pipeline(request, scope.ServiceProvider, ct, Order)();
            }, cancellationToken);
        }

        var result = await next();
        if (request is IInvalidatesCache invalidation && Succeeded(result))
        {
            await cache.RemoveByTagAsync(invalidation.CacheTags.Select(Tag).ToList(), cancellationToken);
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
        var options = query.CacheDuration is { } duration ? new HybridCacheEntryOptions { Expiration = duration, LocalCacheExpiration = duration } : null;
        var caller = ExecutionContext.Capture();
        return await cache.GetOrCreateAsync(
            Key(query, requestType),
            async ct => await InCallerContext(caller, () => load(ct)),
            options,
            query.CacheTags.Select(Tag).ToList(),
            cancellationToken);
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

    private string Key(ICachedQuery query, Type requestType) => query.CacheScope switch
    {
        // a key (its scopes) and a "Log in as" session may see less than the user's own session
        CacheScope.User => $"coworkee:query:{currentUser.TenantId}:{currentUser.UserId}:{Credential()}:{requestType.FullName}:{query.CacheKey}",
        CacheScope.Global => $"coworkee:query:global:{requestType.FullName}:{query.CacheKey}",
        _ => $"coworkee:query:{currentUser.TenantId?.ToString() ?? "anonymous"}:{requestType.FullName}:{query.CacheKey}",
    };

    private string Credential() =>
        (currentUser.ApiKeyId is { } key ? $"key-{key}" : "session") + (currentUser.ImpersonatorId is { } impersonator ? $"-as-{impersonator}" : string.Empty);

    private string Tag(string tag) => $"coworkee:{currentUser.TenantId?.ToString() ?? "anonymous"}:{tag}";

    private static bool Succeeded<TResult>(TResult result) => result is not Result { IsSuccess: false };

    private sealed class NotCached(object result) : Exception
    {
        public object Result { get; } = result;
    }
}
