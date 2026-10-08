# Messaging and pipeline

Every use case is a request that goes through `IDispatcher`. Endpoints, jobs, the AI assistant and tests all send the same requests, so validation, permissions and transactions apply everywhere.

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

`AddMessagingFromAssembly(assembly)` registers all handlers, validators and domain event handlers of a module.

## Commands, queries, results

| Type | Meaning |
|---|---|
| `ICommand<TResult>` | Changes state. Runs in a unit of work: changes are saved once, after the handler. |
| `IQuery<TResult>` | Reads. No save. |
| `Result` / `Result<T>` | Success, or an `Error` with code, message, kind and optional field details |
| `Error.Validation`, `.NotFound`, `.Forbidden`, `.Conflict`, `.Unauthorized`, `.Unexpected` | Map to 400, 404, 403, 409, 401 and 500 in `ToHttpResult` |

Return values convert implicitly: `return brand.ToDto();` and `return BrandErrors.NotFound;` both produce a `Result<BrandDto>`. Expected failures are results, not exceptions.

## The pipeline

Middleware wraps every request in a fixed order:

| Order | Middleware | Does |
|---|---|---|
| 100 | Logging | logs the request, warns when it takes longer than `MessagingOptions.SlowRequestThreshold` (500 ms) |
| 200 | Authorization | checks `[RequiresPermission]` on the request type |
| 300 | Validation | runs all FluentValidation validators, returns `Error.Validation` with field messages |
| 350 | Caching | answers `ICachedQuery` from HybridCache, clears tags of `IInvalidatesCache` after success |
| 400 | Unit of work | saves the `DbContext` after a successful command |

```csharp
[RequiresPermission(CatalogPermissions.Brands.Delete)]
public sealed record DeleteBrandsCommand(IReadOnlyList<Guid> Ids) : ICommand<Result>, IInvalidatesCache
{
    public IReadOnlyList<string> CacheTags => [DashboardCache.Tag];
}
```

### Your own middleware and behaviors

Middleware applies to every request; pick an `Order` between the built-in ones.

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

A behavior is typed and applies to one request, or to all of them as an open generic:

```csharp
internal sealed class AuditBrandChanges : IRequestBehavior<AddEditBrandCommand, Result<BrandDto>>
{
    public async Task<Result<BrandDto>> HandleAsync(AddEditBrandCommand request, RequestHandlerDelegate<Result<BrandDto>> next, CancellationToken cancellationToken)
    {
        var result = await next();
        // react to the outcome of this one use case
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

`CacheScope` decides who shares an entry: `Tenant` (default), `User` or `Global`. Failures are never cached. Commands that change the underlying data list the same tag in `IInvalidatesCache.CacheTags`.

## Domain events

Aggregates raise events; handlers run after the transaction committed, through the outbox, as the user who caused them.

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

The API host processes the outbox with `services.AddCoworkeeOutboxProcessing<MyAppDbContext>()`. Failed events are retried.
