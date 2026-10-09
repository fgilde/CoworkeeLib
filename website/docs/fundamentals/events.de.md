# Events und Sperren

[Domain-Events](messaging.md#domain-events) bleiben innerhalb einer App. Integration-Events gehen über App- und Service-Grenzen; sie nutzen dieselbe Outbox, ein Event wird also erst gesendet, wenn die Änderung, die es ausgelöst hat, gespeichert ist.

## Integration-Events

`Coworkee.EventBus` hinzufügen und von `CoworkeeEventBusModule` abhängen. Die Events gehören in eine Contracts-Assembly, die Sender und Empfänger referenzieren.

```csharp
public sealed record OrderShipped(Guid OrderId, string Number) : IIntegrationEvent;
```

Veröffentlicht wird aus jedem Handler über `IEventBus`. Das Event landet in der Outbox der aktuellen Unit of Work und wird gesendet, nachdem der Command gespeichert hat:

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

Behandelt wird es in jeder App; `AddMessagingFromAssembly` findet die Handler wie alle anderen:

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

| Thema | Verhalten |
|---|---|
| Zustellung | Der Outbox-Prozessor (`AddCoworkeeOutboxProcessing<TContext>()`) sendet das Event. Ohne Transport-Paket bekommen es die Handler derselben App direkt. |
| Benutzer | Handler laufen als der Benutzer und Mandant, der das Event veröffentlicht hat. |
| Idempotenz | Jeder Handler läuft in eigenem Scope und eigener Transaktion. Seine Änderungen werden zusammen mit einer Zeile in `cw."InboxMessages"` (Message-ID, Handler) gespeichert; eine erneut zugestellte Nachricht überspringt Handler, die sie schon verarbeitet haben. `SaveChangesAsync` muss der Handler nicht selbst aufrufen, die Inbox speichert mit. |
| Wiederholungen | Ein fehlschlagender Handler hält die anderen nicht auf. In-Process wiederholt die Outbox die Nachricht mit Backoff bis zu 5-mal; danach bleibt sie mit ihrem Fehler in `cw."OutboxMessages"`. |

## RabbitMQ

`Coworkee.EventBus.RabbitMq` hinzufügen und von `CoworkeeRabbitMqEventBusModule` abhängen. Verbunden wird über den Connection-String `rabbitmq` oder `Coworkee:EventBus:RabbitMq:ConnectionString`.

| Einstellung (`Coworkee:EventBus:RabbitMq`) | Standard | Bedeutung |
|---|---|---|
| `Exchange` | `coworkee.events` | Direct-Exchange, Routing-Key ist der Event-Typ |
| `Queue` | Name der Entry-Assembly | Queue dieser App: Instanzen einer App teilen sie, jede andere App braucht eine eigene |
| `MaxDeliveries` | 5 | Versuche, bevor eine Nachricht nach `{Queue}.dead` wandert |
| `Prefetch` | 16 | unbestätigte Nachrichten pro Consumer |

Die Outbox markiert eine Nachricht erst als gesendet, wenn der Broker sie bestätigt hat. Jede App bindet ihre Quorum-Queue an die Event-Typen, für die sie Handler hat. Eine fehlschlagende Nachricht kommt mit einem Versuchszähler ans Ende der Queue; erreicht sie `MaxDeliveries`, landet sie zur Prüfung in `{Queue}.dead`. Events, die noch niemand abonniert hat, verwirft der Broker.

## Verteilte Sperren

`IDistributedLock` verhindert, dass Arbeit über mehrere Instanzen doppelt läuft, etwa ein wiederkehrender Job, der manuell gestartet wird, während der geplante Lauf noch arbeitet:

```csharp
await using var held = await locks.AcquireAsync("invoice-run", TimeSpan.Zero, cancellationToken);
if (held is null)
{
    return; // eine andere Instanz ist dran
}
```

`AcquireAsync` wartet bis zum Timeout und gibt `null` zurück, wenn die Sperre so lange belegt blieb. Das Freigeben übernimmt `DisposeAsync` des Handles.

| Anbieter | Einrichtung | Hinweise |
|---|---|---|
| Postgres Advisory Lock | Standard mit `AddCoworkeeDbContext` | hält eine Verbindung, solange gesperrt ist; ein abgestürzter Halter gibt sie frei, sobald die Verbindung abbricht |
| Redis | `Coworkee:DistributedLock:Provider = Redis` mit `Coworkee.Realtime` und einem Connection-String `redis` | läuft nach 30 s ab, wenn der Halter sie nicht mehr verlängert |

Der Benachrichtigungs-Digest nutzt sie, damit ein manueller Lauf während des geplanten keine zweite Mail verschickt.
