using Coworkee.Contracts.Realtime;
using Coworkee.Core.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Coworkee.Realtime.Watch;

/// <summary>
/// Looks at each watched set while a connection of this instance follows it ("type:X" or "entity:X:id") and sends what changed as <see cref="RealtimeEventTypes.EntityChanged"/>,
/// like a change made in the app, to the organisations that follow it; a set nobody follows is not read.
/// </summary>
// shortcut: every instance watches for its own connections, so behind a backplane an organisation followed on two instances hears a change twice (clients
// reload once per burst anyway); one watcher per cluster (a distributed lock) when that matters
internal sealed partial class RealtimeWatcher(
    RealtimeWatches watches, RealtimeConnections connections, IRealtimePublisher publisher, IServiceScopeFactory scopes, ILogger<RealtimeWatcher> logger, TimeProvider? clock = null)
    : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(100);

    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(1);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!watches.All.Any())
        {
            return;
        }

        using var timer = new PeriodicTimer(Tick, _clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var state in watches.All.Where(s => s.Due <= _clock.GetUtcNow()))
            {
                await LookAsync(state, stoppingToken);
            }
        }
    }

    internal async Task LookAsync(RealtimeWatches.State state, CancellationToken cancellationToken)
    {
        var name = state.Watch.EntityType.Name;
        var following = connections.Following(name);
        if (following.Count == 0)
        {
            // nobody looks: nothing is read, and the next follower starts from the newest rows then (within a second, not an interval)
            state.Watermark = null;
            state.Due = _clock.GetUtcNow() + (state.Watch.Interval < Idle ? state.Watch.Interval : Idle);
            return;
        }

        state.Due = _clock.GetUtcNow() + state.Watch.Interval;

        try
        {
            using var system = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
            await using var scope = scopes.CreateAsyncScope();
            var round = await state.Watch.ReadAsync(scope.ServiceProvider, state.Watermark, cancellationToken);
            state.Polled();
            var primed = state.Watermark is not null;
            state.Watermark = round.Watermark ?? state.Watermark ?? DateTimeOffset.MinValue;
            if (primed)
            {
                await SendAsync(name, round, following, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(exception, name);
        }
    }

    private async Task SendAsync(string name, RealtimeWatchRound round, IReadOnlyCollection<(Guid? TenantId, string Topic)> following, CancellationToken cancellationToken)
    {
        var type = RealtimeTopics.Type(name);
        if (round.Many)
        {
            // too many to send one by one: the set as a whole changed
            LogWholeSet(name, round.Why);
            foreach (var tenant in following.Where(f => f.Topic == type).Select(f => f.TenantId).Distinct())
            {
                await publisher.PublishAsync(tenant, type, RealtimeEventTypes.EntityChanged, new EntityChangedPayload(name, string.Empty, EntityChangedPayload.Updated, []), cancellationToken);
            }

            return;
        }

        foreach (var change in round.Changes)
        {
            var payload = new EntityChangedPayload(name, change.Id, change.Action, []);
            var entity = RealtimeTopics.Entity(name, change.Id);
            foreach (var (tenant, topic) in following.Where(f => (f.Topic == type || f.Topic == entity) && (change.TenantId is null || f.TenantId == change.TenantId)))
            {
                await publisher.PublishAsync(tenant, topic, RealtimeEventTypes.EntityChanged, payload, cancellationToken);
            }
        }
    }

    [LoggerMessage(2, LogLevel.Information, "{EntityType} changed as a whole: {Why}")]
    private partial void LogWholeSet(string entityType, string? why);

    [LoggerMessage(1, LogLevel.Warning, "Watching {EntityType} for changes failed")]
    private partial void LogFailed(Exception exception, string entityType);
}
