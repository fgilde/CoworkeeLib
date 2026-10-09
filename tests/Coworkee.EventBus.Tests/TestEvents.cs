using System.Collections.Concurrent;
using Coworkee.Core.Security;

namespace Coworkee.EventBus.Tests;

public sealed record OrderPlaced(Guid OrderId) : IIntegrationEvent;

public sealed record OrderCancelled(Guid OrderId) : IIntegrationEvent;

public sealed class Recorder
{
    public ConcurrentQueue<(string Handler, object Event, Guid? UserId)> Received { get; } = [];

    public int FailuresLeft { get; set; }
}

internal sealed class OrderPlacedHandler(Recorder recorder, ICurrentUser user) : IIntegrationEventHandler<OrderPlaced>
{
    public Task HandleAsync(OrderPlaced integrationEvent, CancellationToken cancellationToken)
    {
        recorder.Received.Enqueue((nameof(OrderPlacedHandler), integrationEvent, user.UserId));
        return Task.CompletedTask;
    }
}

internal sealed class SteadyCancelledHandler(Recorder recorder) : IIntegrationEventHandler<OrderCancelled>
{
    public Task HandleAsync(OrderCancelled integrationEvent, CancellationToken cancellationToken)
    {
        recorder.Received.Enqueue((nameof(SteadyCancelledHandler), integrationEvent, null));
        return Task.CompletedTask;
    }
}

internal sealed class FlakyCancelledHandler(Recorder recorder) : IIntegrationEventHandler<OrderCancelled>
{
    public Task HandleAsync(OrderCancelled integrationEvent, CancellationToken cancellationToken)
    {
        if (recorder.FailuresLeft-- > 0)
        {
            throw new InvalidOperationException("down");
        }

        recorder.Received.Enqueue((nameof(FlakyCancelledHandler), integrationEvent, null));
        return Task.CompletedTask;
    }
}
