# Events and locks

[Domain events](messaging.md#domain-events) stay inside one app. Integration events cross app and service boundaries; they use the same outbox, so an event is only sent when the change that caused it is saved.

## Integration events

Add `Coworkee.EventBus` and depend on `CoworkeeEventBusModule`. Put the events in a contracts assembly that publisher and subscribers reference.

```csharp
public sealed record OrderShipped(Guid OrderId, string Number) : IIntegrationEvent;
```

Publish from any handler through `IEventBus`. The event goes into the outbox of the current unit of work and is sent after the command saved:

```csharp
internal sealed class ShipOrderHandler(CoworkeeDbContext db, IEventBus bus) : IHandler<ShipOrder, Result>
{
    public async Task<Result> HandleAsync(ShipOrder command, CancellationToken cancellationToken)
    {
        var order = await db.Set<Order>().SingleAsync(o => o.Id == command.Id, cancellationToken);
        order.Ship();
        await bus.PublishAsync(new OrderShipped(order.Id, order.Number), cancellationToken);
        return Result.Success();
    }
}
```

Handle it in any app; `AddMessagingFromAssembly` finds the handlers like all others:

```csharp
internal sealed class CreateInvoice(CoworkeeDbContext db) : IIntegrationEventHandler<OrderShipped>
{
    public Task HandleAsync(OrderShipped integrationEvent, CancellationToken cancellationToken)
    {
        db.Add(new Invoice { OrderId = integrationEvent.OrderId });
        return Task.CompletedTask;
    }
}
```

| Topic | Behavior |
|---|---|
| Delivery | The outbox processor (`AddCoworkeeOutboxProcessing<TContext>()`) sends the event. Without a transport package the handlers of the same app get it directly. |
| User | Handlers run as the user and tenant that published the event. |
| Idempotency | Each handler runs in its own scope and transaction. Its changes are saved together with a row in `cw."InboxMessages"` (message id, handler); a redelivered message skips handlers that already processed it. The handler does not need to call `SaveChangesAsync`, the inbox saves its changes. |
| Retries | A failing handler does not stop the others. In process, the outbox retries the message with backoff, up to 5 times; then it stays in `cw."OutboxMessages"` with its error. |

## RabbitMQ

Add `Coworkee.EventBus.RabbitMq` and depend on `CoworkeeRabbitMqEventBusModule`. It connects with the `rabbitmq` connection string or `Coworkee:EventBus:RabbitMq:ConnectionString`.

| Setting (`Coworkee:EventBus:RabbitMq`) | Default | Meaning |
|---|---|---|
| `Exchange` | `coworkee.events` | direct exchange, routing key is the event type |
| `Queue` | entry assembly name | queue of this app: instances of one app share it, every other app needs its own |
| `MaxDeliveries` | 5 | attempts before a message moves to `{Queue}.dead` |
| `Prefetch` | 16 | unacknowledged messages per consumer |

The outbox only marks a message as sent when the broker confirmed it. Each app binds its quorum queue to the event types it has handlers for. A failing message goes back to the end of the queue with an attempt counter; once it reaches `MaxDeliveries` it lands in `{Queue}.dead` for inspection. Events nobody subscribed to yet are dropped by the broker.

## Distributed locks

`IDistributedLock` keeps work from running twice across instances, for example a recurring job started manually while the scheduled run is still going:

```csharp
await using var held = await locks.AcquireAsync("invoice-run", TimeSpan.Zero, cancellationToken);
if (held is null)
{
    return; // another instance is on it
}
```

`AcquireAsync` waits up to the timeout and returns `null` when the lock stayed taken. Disposing the handle releases it.

| Provider | Setup | Notes |
|---|---|---|
| Postgres advisory lock | default with `AddCoworkeeDbContext` | holds one connection while locked; a crashed holder frees it when the connection drops |
| Redis | `Coworkee:DistributedLock:Provider = Redis` with `Coworkee.Realtime` and a `redis` connection string | expires after 30 s unless the holder is alive and extends it |

The notification digest uses it, so a manual run during the scheduled one sends no second mail.
