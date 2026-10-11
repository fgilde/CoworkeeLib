using System.Text.Json;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Coworkee.Realtime;

/// <summary>
/// HybridCache whose removals reach every instance: a removed key or tag goes out on Redis pub/sub, and the other instances drop
/// it from their local level. Permission, setting, feature and text caches change at once everywhere instead of after their lifetime.
/// </summary>
internal sealed partial class RedisHybridCache(HybridCache inner, IServiceProvider services, ILogger<RedisHybridCache> logger) : HybridCache
{
    private static readonly RedisChannel Channel = RedisChannel.Literal("coworkee:cache-invalidation");

    private readonly string _instance = Guid.NewGuid().ToString("N");
    private readonly Lock _lock = new();
    private ISubscriber? _subscriber;

    public override ValueTask<T> GetOrCreateAsync<TState, T>(
        string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        Subscribe();
        return inner.GetOrCreateAsync(key, state, factory, options, tags, cancellationToken);
    }

    public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default)
    {
        Subscribe();
        return inner.SetAsync(key, value, options, tags, cancellationToken);
    }

    public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default) => RemoveAsync([key], cancellationToken);

    public override async ValueTask RemoveAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        var list = keys.ToList();
        await inner.RemoveAsync(list, cancellationToken);
        await PublishAsync(new Message(_instance, list, []));
    }

    public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) => RemoveByTagAsync([tag], cancellationToken);

    public override async ValueTask RemoveByTagAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default)
    {
        var list = tags.ToList();
        await inner.RemoveByTagAsync(list, cancellationToken);
        await PublishAsync(new Message(_instance, [], list));
    }

    // an instance subscribes with its first cache entry: before that it holds nothing others could invalidate
    private void Subscribe()
    {
        if (_subscriber is not null)
        {
            return;
        }

        lock (_lock)
        {
            if (_subscriber is not null)
            {
                return;
            }

            try
            {
                var subscriber = services.GetRequiredService<IConnectionMultiplexer>().GetSubscriber();
                subscriber.Subscribe(Channel, (channel, value) => _ = ReceiveAsync(value));
                _subscriber = subscriber;
            }
            catch (Exception exception) when (exception is RedisException or TimeoutException)
            {
                LogSubscribeFailed(exception);
            }
        }
    }

    private async Task ReceiveAsync(RedisValue value)
    {
        try
        {
            if (JsonSerializer.Deserialize<Message>(value.ToString()) is { } message && message.Instance != _instance)
            {
                await inner.RemoveAsync(message.Keys);
                await inner.RemoveByTagAsync(message.Tags);
            }
        }
        catch (Exception exception)
        {
            LogReceiveFailed(exception);
        }
    }

    private async Task PublishAsync(Message message)
    {
        try
        {
            await services.GetRequiredService<IConnectionMultiplexer>().GetSubscriber().PublishAsync(Channel, JsonSerializer.Serialize(message));
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            LogPublishFailed(exception);
        }
    }

    private sealed record Message(string Instance, List<string> Keys, List<string> Tags);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Subscribing to cache invalidations on Redis failed; other instances' changes reach this one within the cache lifetime")]
    private partial void LogSubscribeFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publishing a cache invalidation on Redis failed; other instances notice it within the cache lifetime")]
    private partial void LogPublishFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Dropping a cache entry another instance invalidated failed")]
    private partial void LogReceiveFailed(Exception exception);
}
