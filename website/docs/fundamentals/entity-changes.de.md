# Änderungen an Entities

Jedes Speichern wird einmal durchsucht. Jede angelegte, geänderte oder gelöschte Entity wird zu einer `EntityChange`: was (Typ, Id, Aktion, geänderte Werte), wer (`Actor`), wann (`At`), warum (`Reason`), der Trace der Anfrage (`CorrelationId`) und die Organisation (`TenantId`). Audit-Protokoll, Realtime, Webhooks und Elsa-Workflows werden aus diesem einen Durchlauf gespeist, und eigener Code kann ihm ebenfalls zuhören, dauerhaft oder im Prozess.

```csharp
// die Form (Coworkee.Application.Events)
public record EntityChange : IDomainEvent
{
    string EntityType;                       // Name des CLR-Wurzeltyps, wie in Realtime-Themen: type:{EntityType}
    string EntityId;                         // der Schlüssel, Teile durch Kommas getrennt
    ChangeAction Action;                     // Created | Updated | Deleted (auch weich) | Restored (weiches Löschen zurückgenommen)
    IReadOnlyList<PropertyChange> Changes;   // (Property, OldValue, NewValue) als JSON, maskiert wie im Audit-Protokoll
    ActorInfo? Actor;                        // (UserId, Name, ImpersonatorId); null für das System und anonyme Datensätze
    DateTimeOffset At; string? Reason; string? CorrelationId; Guid? TenantId;
}
```

`Changes` folgen den [Audit-Regeln](../modules/auditing.md#audit-protokoll): `[Sensitive]`-Werte sind `"***"`, `[NotAudited]`-Typen und -Felder tragen keine Werte, Owned-Werte kommen als ein JSON-Objekt.

## Dauerhafte Handler

Die Änderungen eines Typs veröffentlichen, dann behandeln. Jede Änderung eines veröffentlichten Typs schreibt im selben Speichern eine Outbox-Zeile; der Handler läuft nach dem Commit, einmal, bei Fehlern erneut, als der Benutzer und die Organisation, die gespeichert haben, auch auf einer anderen Instanz nach einem Neustart. Nicht veröffentlichte Typen kosten nichts.

```csharp
// im Modul
services.AddCoworkeeEvents(e => e.PublishChanges<Order>());   // oder [PublishChanges] an der Entity; abgeleitete Typen zählen mit

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

Handler werden wie alle anderen gefunden (`AddMessagingFromAssembly`). `IEntityChangeHandler<T>` ist ein `IDomainEventHandler<EntityChange<T>>`, es gelten also derselbe Outbox-Prozessor, dieselben Wiederholungen und Metadaten.

## Zuhörer im Prozess

`IEntityEvents` meldet Code in diesem Prozess jedes Speichern nach seinem Commit: nichts wird gespeichert oder wiederholt, andere Instanzen hören nichts. Gut für Hosted Services, Caches, Diagnose und Tests:

```csharp
using var subscription = entityEvents.Subscribe<EntityChange<Order>>((change, ct) => cache.EvictAsync(change.EntityId, ct));
entityEvents.Subscribe<EntityChange>((change, ct) => { metrics.Count(change.EntityType, change.Action); return Task.CompletedTask; });
```

Ein fehlerhafter Zuhörer wird protokolliert und lässt das Speichern nie scheitern. Ohne Zuhörer wird nichts aufgehoben.

## Warum: der Änderungsgrund

```csharp
using (ChangeReason.Begin("Import from CW1"))
{
    await importer.RunAsync(ct);   // jede darin gespeicherte Änderung trägt den Grund
}

public sealed record CancelOrder(Guid Id, string? Reason) : ICommand<Result>, IHasChangeReason;   // die Pipeline öffnet den Bereich
```

Der Grund landet auf jeder `EntityChange` und jedem Audit-Eintrag (`cw.AuditEntries.Reason`, höchstens 500 Zeichen), in `GET /api/v1/audit` (`AuditEntryDto.Reason`), im Verlauf eines Datensatzes (`EntityHistoryEntryDto.Reason`) und unter den Einträgen von `AuditTimeline` und `CoworkeeEntityHistory`.

## Senken

Der Sammler läuft dort, wo das Audit-Protokoll schon immer lief (nach den Interceptoren für weiches Löschen, Audit-Felder und Organisation, vor Schnappschüssen und Outbox). Der letzte Interceptor des Speicherns gibt die Änderungen an die `IEntityChangeSink`s: im Speichern (Zeilen, die sie anlegen, werden mit der Änderung gespeichert), nach dem Commit, oder verworfen bei einem Fehler oder Rollback. Die Senken der Bibliothek sind Audit-Protokoll, Realtime (`EntityChanged`-Nutzlasten unverändert), Webhooks (`WebhookEntityChanged` unverändert), Elsa (`Watch<T>` schreibt `Coworkee.Workflows.Elsa.EntityChanged` wie bisher, ein weiches Löschen bleibt dort `Updated`) und die beiden oben. Eine eigene Senke:

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

`ExecuteUpdate` und `ExecuteDelete` umgehen den Change Tracker: Sie erzeugen keine Änderung, wie bisher.
