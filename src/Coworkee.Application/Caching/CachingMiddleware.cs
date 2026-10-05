using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using System.Reflection;
using Microsoft.Extensions.Caching.Hybrid;

namespace Coworkee.Application.Caching;

internal sealed class CachingMiddleware(HybridCache cache, ICurrentUser currentUser) : IRequestMiddleware
{
    public int Order => MiddlewareOrder.Caching;

    public async Task<TResult> InvokeAsync<TRequest, TResult>(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
        where TRequest : IRequest<TResult>
    {
        if (request is ICachedQuery query && (query.CacheScope != CacheScope.User || currentUser.UserId is not null))
        {
            return await ReadThroughAsync(query, typeof(TRequest), next, cancellationToken);
        }

        var result = await next();
        if (request is IInvalidatesCache invalidation && Succeeded(result))
        {
            await cache.RemoveByTagAsync(invalidation.CacheTags.Select(Tag).ToList(), cancellationToken);
        }

        return result;
    }

    private Task<TResult> ReadThroughAsync<TResult>(ICachedQuery query, Type requestType, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
    {
        if (typeof(TResult).IsGenericType && typeof(TResult).GetGenericTypeDefinition() == typeof(Result<>))
        {
            var read = ReadResultMethods.GetOrAdd(typeof(TResult), type => ReadResultMethod.MakeGenericMethod(type.GetGenericArguments()[0]));
            return (Task<TResult>)read.Invoke(this, [query, requestType, next, cancellationToken])!;
        }

        return ReadAsync(query, requestType, () => next(), _ => true, cancellationToken);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, MethodInfo> ReadResultMethods = new();

    private static readonly MethodInfo ReadResultMethod =
        typeof(CachingMiddleware).GetMethod(nameof(ReadResultAsync), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private async Task<Result<TValue>> ReadResultAsync<TValue>(ICachedQuery query, Type requestType, RequestHandlerDelegate<Result<TValue>> next, CancellationToken cancellationToken)
    {
        Result<TValue>? failure = null;
        var value = await ReadAsync<TValue>(
            query,
            requestType,
            async () =>
            {
                var result = await next();
                failure = result.IsSuccess ? null : result;
                return result.IsSuccess ? result.Value : default!;
            },
            _ => failure is null,
            cancellationToken);
        return failure ?? Result<TValue>.Success(value);
    }

    private async Task<TValue> ReadAsync<TValue>(
        ICachedQuery query, Type requestType, Func<Task<TValue>> load, Func<TValue, bool> cacheable, CancellationToken cancellationToken)
    {
        var options = query.CacheDuration is { } duration ? new HybridCacheEntryOptions { Expiration = duration, LocalCacheExpiration = duration } : null;
        try
        {
            return await cache.GetOrCreateAsync(
                Key(query, requestType),
                async _ => await load() is var value && cacheable(value) ? value : throw new NotCached<TValue>(value),
                options,
                query.CacheTags.Select(Tag).ToList(),
                cancellationToken);
        }
        catch (NotCached<TValue> notCached)
        {
            return notCached.Value;
        }
    }

    private string Key(ICachedQuery query, Type requestType) => query.CacheScope switch
    {
        CacheScope.User => $"coworkee:query:{currentUser.TenantId}:{currentUser.UserId}:{requestType.FullName}:{query.CacheKey}",
        CacheScope.Global => $"coworkee:query:global:{requestType.FullName}:{query.CacheKey}",
        _ => $"coworkee:query:{currentUser.TenantId?.ToString() ?? "anonymous"}:{requestType.FullName}:{query.CacheKey}",
    };

    private string Tag(string tag) => $"coworkee:{currentUser.TenantId?.ToString() ?? "anonymous"}:{tag}";

    private static bool Succeeded<TResult>(TResult result) => result is not Result { IsSuccess: false };

    private sealed class NotCached<TValue>(TValue value) : Exception
    {
        public TValue Value { get; } = value;
    }
}
