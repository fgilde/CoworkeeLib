using System.Security.Claims;
using System.Text.Json;
using Coworkee.Contracts.Realtime;
using Coworkee.Realtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Coworkee.Identity.Users;

/// <summary>
/// A user whose security stamp changed; <paramref name="Reason"/> is "locked", "password-changed" or "signed-out", <paramref name="KeptSessionId"/> the session
/// that changed its own password and goes on.
/// </summary>
public sealed record SessionChange(Guid UserId, string Reason, string? KeptSessionId = null);

/// <summary>
/// Tells every instance that security stamps changed: each drops the cached stamps and ends the realtime connections of the sessions
/// that no longer count. Through Redis pub/sub when a <c>redis</c> connection string is configured, otherwise this instance only.
/// </summary>
public sealed partial class SessionSignal(SessionStamps stamps, IServiceProvider services, ILogger<SessionSignal> logger) : IHostedService
{
    private static readonly RedisChannel Channel = RedisChannel.Literal("coworkee:sessions");

    private readonly string _instance = Guid.NewGuid().ToString("N");

    public async Task PublishAsync(IReadOnlyCollection<SessionChange> changes, CancellationToken cancellationToken)
    {
        await HandleAsync(changes, cancellationToken);
        try
        {
            if (services.GetService<IConnectionMultiplexer>() is { } redis)
            {
                await redis.GetSubscriber().PublishAsync(Channel, JsonSerializer.Serialize(new Message(_instance, changes)));
            }
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            // the change is saved; the other instances notice within the cache lifetime
            LogPublishFailed(exception);
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (services.GetService<IConnectionMultiplexer>() is { } redis)
            {
                await redis.GetSubscriber().SubscribeAsync(Channel, (channel, value) => _ = ReceiveAsync(value));
            }
        }
        catch (Exception exception) when (exception is RedisException or TimeoutException)
        {
            LogSubscribeFailed(exception);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task ReceiveAsync(RedisValue value)
    {
        try
        {
            if (JsonSerializer.Deserialize<Message>(value.ToString()) is { } message && message.Instance != _instance)
            {
                await HandleAsync(message.Changes, CancellationToken.None);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogReceiveFailed(exception);
        }
    }

    private async Task HandleAsync(IEnumerable<SessionChange> changes, CancellationToken cancellationToken)
    {
        var connections = services.GetService<RealtimeConnections>();
        var publisher = services.GetService<IRealtimePublisher>();
        foreach (var change in changes)
        {
            await stamps.ForgetAsync(change.UserId, cancellationToken);
            if (connections is not null)
            {
                await connections.EndAsync(change.UserId, principal => KeepsAsync(change.UserId, principal, cancellationToken), change.Reason, cancellationToken, change.KeptSessionId);
            }

            // for decorators of the publisher (an app's own hub ends its connections of this instance); RealtimePublisher sends it nowhere, the
            // RealtimeHub connections that ended got it above
            try
            {
                await (publisher?.PublishAsync(null, RealtimeTopics.User(change.UserId), RealtimeEventTypes.SessionRevoked,
                    new SessionRevokedPayload(change.Reason, change.KeptSessionId), cancellationToken) ?? Task.CompletedTask);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogForwardFailed(exception);
            }
        }
    }

    // connections without a stamp come from tokens that cannot be checked, they end with the sessions
    private async Task<bool> KeepsAsync(Guid userId, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
        principal.FindFirstValue(SessionStamp.ClaimType) is { } stamp
        && SessionStamp.IsCurrent(await stamps.GetAsync(userId, cancellationToken), stamp, principal.FindFirstValue(SessionStamp.SessionClaimType));

    private sealed record Message(string Instance, IReadOnlyCollection<SessionChange> Changes);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Subscribing to session changes on Redis failed; other instances' changes reach this one only through the cache lifetime")]
    private partial void LogSubscribeFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publishing a session change on Redis failed; other instances notice it within the cache lifetime")]
    private partial void LogPublishFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Handling a session change from another instance failed")]
    private partial void LogReceiveFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publishing a session revocation through the realtime publisher failed")]
    private partial void LogForwardFailed(Exception exception);
}
