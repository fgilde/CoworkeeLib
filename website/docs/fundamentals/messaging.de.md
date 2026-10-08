# Messaging und Pipeline

Jeder Anwendungsfall ist ein Request, der über `IDispatcher` läuft. Endpunkte, Jobs, der KI-Assistent und Tests schicken dieselben Requests, deshalb gelten Validierung, Berechtigungen und Transaktionen überall.

```csharp
public sealed record GetBrandByIdQuery(Guid Id) : IQuery<Result<BrandDto>>;

internal sealed class GetBrandByIdHandler(CoworkeeDbContext db) : IHandler<GetBrandByIdQuery, Result<BrandDto>>
{
    public async Task<Result<BrandDto>> HandleAsync(GetBrandByIdQuery query, CancellationToken cancellationToken) =>
        await db.Set<Brand>().SingleOrDefaultAsync(b => b.Id == query.Id, cancellationToken) is { } brand
            ? brand.ToDto()
            : BrandErrors.NotFound;
}
```

```csharp
var result = await dispatcher.SendAsync(new GetBrandByIdQuery(id), cancellationToken);
```

`AddMessagingFromAssembly(assembly)` registriert alle Handler, Validatoren und Domain-Event-Handler eines Moduls.

## Commands, Queries, Results

| Typ | Bedeutung |
|---|---|
| `ICommand<TResult>` | Ändert Zustand. Läuft in einer Unit of Work: Änderungen werden einmal gespeichert, nach dem Handler. |
| `IQuery<TResult>` | Liest. Kein Speichern. |
| `Result` / `Result<T>` | Erfolg oder ein `Error` mit Code, Text, Art und optionalen Feld-Details |
| `Error.Validation`, `.NotFound`, `.Forbidden`, `.Conflict`, `.Unauthorized`, `.Unexpected` | Werden in `ToHttpResult` zu 400, 404, 403, 409, 401 und 500 |

Rückgabewerte werden implizit umgewandelt: `return brand.ToDto();` und `return BrandErrors.NotFound;` ergeben beide ein `Result<BrandDto>`. Erwartbare Fehler sind Results, keine Exceptions.

## Die Pipeline

Middleware umschließt jeden Request in fester Reihenfolge:

| Order | Middleware | Aufgabe |
|---|---|---|
| 100 | Logging | protokolliert den Request, warnt ab `MessagingOptions.SlowRequestThreshold` (500 ms) |
| 200 | Authorization | prüft `[RequiresPermission]` am Request-Typ |
| 300 | Validation | führt alle FluentValidation-Validatoren aus, liefert `Error.Validation` mit Feldmeldungen |
| 350 | Caching | beantwortet `ICachedQuery` aus dem HybridCache, leert nach Erfolg die Tags von `IInvalidatesCache` |
| 400 | Unit of Work | speichert den `DbContext` nach einem erfolgreichen Command |

```csharp
[RequiresPermission(CatalogPermissions.Brands.Delete)]
public sealed record DeleteBrandsCommand(IReadOnlyList<Guid> Ids) : ICommand<Result>, IInvalidatesCache
{
    public IReadOnlyList<string> CacheTags => [DashboardCache.Tag];
}
```

### Eigene Middleware und Behaviors

Middleware gilt für jeden Request; wählen Sie eine `Order` zwischen den eingebauten.

```csharp
internal sealed class TimingMiddleware(ILogger<TimingMiddleware> logger) : IRequestMiddleware
{
    public int Order => MiddlewareOrder.Validation + 10;

    public async Task<TResult> InvokeAsync<TRequest, TResult>(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
        where TRequest : IRequest<TResult>
    {
        var started = Stopwatch.GetTimestamp();
        var result = await next();
        logger.LogDebug("{Request} took {Elapsed}", typeof(TRequest).Name, Stopwatch.GetElapsedTime(started));
        return result;
    }
}

services.AddRequestMiddleware<TimingMiddleware>();
```

Ein Behavior ist typisiert und gilt für einen Request oder, als offener Generic, für alle:

```csharp
internal sealed class AuditBrandChanges : IRequestBehavior<AddEditBrandCommand, Result<BrandDto>>
{
    public async Task<Result<BrandDto>> HandleAsync(AddEditBrandCommand request, RequestHandlerDelegate<Result<BrandDto>> next, CancellationToken cancellationToken)
    {
        var result = await next();
        // auf das Ergebnis genau dieses Anwendungsfalls reagieren
        return result;
    }
}

services.AddRequestBehavior<AddEditBrandCommand, Result<BrandDto>, AuditBrandChanges>();
services.AddRequestBehavior(typeof(TracingBehavior<,>));
```

## Caching

```csharp
[RequiresPermission(CatalogPermissions.Dashboards.View)]
public sealed record GetDashboardQuery : IQuery<Result<DashboardDto>>, ICachedQuery
{
    public string CacheKey => "dashboard";

    public TimeSpan? CacheDuration => TimeSpan.FromMinutes(5);

    public IReadOnlyList<string> CacheTags => [DashboardCache.Tag];
}
```

`CacheScope` legt fest, wer einen Eintrag teilt: `Tenant` (Standard), `User` oder `Global`. Fehler werden nie gecacht. Commands, die die zugrunde liegenden Daten ändern, nennen dasselbe Tag in `IInvalidatesCache.CacheTags`.

## Domain-Events

Aggregate lösen Events aus; Handler laufen nach dem Commit über die Outbox, als der Benutzer, der sie verursacht hat.

```csharp
public sealed class Order : AuditedAggregateRoot
{
    public void Ship() => Raise(new OrderShipped(Id));
}

public sealed record OrderShipped(Guid OrderId) : IDomainEvent;

internal sealed class NotifyCustomer(IMailSender mails) : IDomainEventHandler<OrderShipped>
{
    public Task HandleAsync(OrderShipped domainEvent, CancellationToken cancellationToken) => /* ... */ Task.CompletedTask;
}
```

Der API-Host verarbeitet die Outbox mit `services.AddCoworkeeOutboxProcessing<MyAppDbContext>()`. Fehlgeschlagene Events werden erneut versucht.
