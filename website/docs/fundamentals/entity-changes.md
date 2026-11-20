# Entity changes

Every save is scanned once. Each added, changed or deleted entity becomes an `EntityChange`: what (type, id, action, changed values), who (`Actor`), when (`At`), why (`Reason`), the request's trace (`CorrelationId`) and the organisation (`TenantId`). The audit trail, realtime, webhooks and Elsa workflows are fed by this scan, and your code can listen to it too, durably or in-process.

```csharp
// the shape (Coworkee.Application.Events)
public record EntityChange : IDomainEvent
{
    string EntityType;                       // root CLR type name, as in realtime topics: type:{EntityType}
    string EntityId;                         // the key, parts joined by commas
    ChangeAction Action;                     // Created | Updated | Deleted (also soft) | Restored (soft delete undone)
    IReadOnlyList<PropertyChange> Changes;   // (Property, OldValue, NewValue) as JSON, masked like the audit trail
    ActorInfo? Actor;                        // (UserId, Name, ImpersonatorId); null for the system and anonymous records
    DateTimeOffset At; string? Reason; string? CorrelationId; Guid? TenantId;
}
```

`Changes` follow the [audit rules](../modules/auditing.md#audit-trail): `[Sensitive]` values are `"***"`, `[NotAudited]` types and properties carry no values, owned values arrive as one JSON object.

## Durable handlers

Publish the changes of a type, then handle them. Every change of a published type writes one outbox row in the same save; the handler runs after the commit, once, retried when it fails, as the user and organisation that saved it, also on another instance after a restart. Types that are not published cost nothing.

```csharp
// in the module
services.AddCoworkeeEvents(e => e.PublishChanges<Order>());   // or [PublishChanges] on the entity; derived types count

public sealed class NotifyWarehouse(INotifier notifier) : IEntityChangeHandler<Order>
{
    public async Task HandleAsync(EntityChange<Order> change, CancellationToken ct)
    {
        if (change.Action == ChangeAction.Updated && change.Changes.Any(c => c.Property == nameof(Order.Status)))
        {
            await notifier.SendAsync(new NotificationRequest(EventTarget.Role("Warehouse"), "Order {0} changed its status")
                { Arguments = [change.EntityId] }, ct);
        }
    }
}
```

Handlers are found like every other handler (`AddMessagingFromAssembly`). `IEntityChangeHandler<T>` is an `IDomainEventHandler<EntityChange<T>>`, so the same outbox processor, retries and metadata apply.

## In-process subscribers

`IEntityEvents` tells code in this process about every save after its commit: nothing is stored or retried, other instances hear nothing. Good for hosted services, caches, diagnostics and tests:

```csharp
using var subscription = entityEvents.Subscribe<EntityChange<Order>>((change, ct) => cache.EvictAsync(change.EntityId, ct));
entityEvents.Subscribe<EntityChange>((change, ct) => { metrics.Count(change.EntityType, change.Action); return Task.CompletedTask; });
```

A failing subscriber is logged and never fails the save. Without subscribers nothing is kept.

## Why: the change reason

```csharp
using (ChangeReason.Begin("Import from CW1"))
{
    await importer.RunAsync(ct);   // every change saved inside carries the reason
}

public sealed record CancelOrder(Guid Id, string? Reason) : ICommand<Result>, IHasChangeReason;   // the pipeline opens the scope
```

The reason lands on every `EntityChange` and audit entry (`cw.AuditEntries.Reason`, at most 500 characters), in `GET /api/v1/audit` (`AuditEntryDto.Reason`), in the record history (`EntityHistoryEntryDto.Reason`) and below the entries of `AuditTimeline` and `CoworkeeEntityHistory`.

## Sinks

The collector runs where the audit trail always ran (after the soft delete, audit field and tenant interceptors, before snapshots and the outbox). The last interceptor of the save hands the changes to the `IEntityChangeSink`s: inside the save (rows they add are saved with the change), after the commit, or discarded on a failure or rollback. The library's sinks are the audit trail, realtime (`EntityChanged` payloads unchanged), webhooks (`WebhookEntityChanged` unchanged), Elsa (`Watch<T>` writes `Coworkee.Workflows.Elsa.EntityChanged` as before, a soft delete stays `Updated` there) and the two above. A sink of your own:

```csharp
internal sealed class SearchIndexSink(SearchQueue queue) : IEntityChangeSink
{
    private readonly List<EntityChange> _pending = [];

    public void Saving(DbContext context, IReadOnlyList<EntityChangeEntry> changes) => _pending.AddRange(changes.Where(c => c.Change.EntityType == "Product").Select(c => c.Change));

    public async Task CommittedAsync(CancellationToken ct) { await queue.EnqueueAsync(_pending, ct); _pending.Clear(); }

    public void Discarded() => _pending.Clear();
}

services.AddScoped<IEntityChangeSink, SearchIndexSink>();
```

`ExecuteUpdate` and `ExecuteDelete` skip the change tracker: they produce no change, as before.
