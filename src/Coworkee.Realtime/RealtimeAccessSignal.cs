using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Coworkee.Realtime;

/// <summary>
/// Permissions changed in an organisation (or everywhere): every instance asks the topic authorizers again for the topics its
/// connections follow (<see cref="RealtimeConnections.RecheckAsync"/>). Through Redis pub/sub when a <c>redis</c> connection string
/// is configured, otherwise this instance only.
/// </summary>
public sealed partial class RealtimeAccessSignal(RealtimeConnections connections, IServiceProvider services, ILogger<RealtimeAccessSignal> logger)
    : IHostedService
{
    // ponytail: waits until the caches of the change (permissions, settings) are dropped by their own interceptors after the commit
    internal static TimeSpan Settle { get; set; } = TimeSpan.FromMilliseconds(300);

    private static readonly RedisChannel Channel = RedisChannel.Literal("coworkee:realtime-access");

    private readonly string _instance = Guid.NewGuid().ToString("N");

    public async Task PublishAsync(Guid? tenantId)
    {
        Recheck(tenantId);
        try
        {
            if (services.GetService<IConnectionMultiplexer>() is { } redis)
            {
                await redis.GetSubscriber().PublishAsync(Channel, JsonSerializer.Serialize(new Message(_instance, tenantId)));
            }
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            LogPublishFailed(exception);
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (services.GetService<IConnectionMultiplexer>() is { } redis)
            {
                await redis.GetSubscriber().SubscribeAsync(Channel, (_, value) =>
                {
                    if (JsonSerializer.Deserialize<Message>(value.ToString()) is { } message && message.Instance != _instance)
                    {
                        Recheck(message.TenantId);
                    }
                });
            }
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            LogSubscribeFailed(exception);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Recheck(Guid? tenantId) => _ = Task.Run(async () =>
    {
        try
        {
            await Task.Delay(Settle);
            await connections.RecheckAsync(tenantId, CancellationToken.None);
        }
        catch (Exception exception)
        {
            LogRecheckFailed(exception);
        }
    });

    private sealed record Message(string Instance, Guid? TenantId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Subscribing to permission changes on Redis failed; connections of this instance keep their topics until they reconnect")]
    private partial void LogSubscribeFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publishing a permission change on Redis failed; connections of other instances keep their topics until they reconnect")]
    private partial void LogPublishFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Checking the realtime topics again after a permission change failed")]
    private partial void LogRecheckFailed(Exception exception);
}
