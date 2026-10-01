<p align="center">
  <img src="assets/icon.svg" width="120" alt="Coworkee logo" />
</p>

<h1 align="center">Coworkee</h1>

<p align="center">A small, modular application framework for .NET 10.<br/>Modules, a request dispatcher, EF Core conventions with audit and outbox, ASP.NET Core and Aspire integration.</p>

---

## Packages

| Package | What it gives you |
|---|---|
| `Coworkee.Core` | Module system with `[DependsOn]`, `Result` / `Error`, current user and ambient user scope |
| `Coworkee.Domain` | `Entity`, `AggregateRoot` with domain events, markers for audit, soft delete, tenants and concurrency |
| `Coworkee.Application` | Dispatcher for commands and queries with logging, validation and unit-of-work middleware |
| `Coworkee.Infrastructure` | `CoworkeeDbContext`, interceptors, field-level audit trail, transactional outbox with retries |
| `Coworkee.AspNetCore` | `AddCoworkee<TRoot>()`, result to problem-details mapping, OpenAPI and Swagger UI |
| `Coworkee.Testing` | Postgres test container fixture, test user, web application factory |
| `Coworkee.Aspire` | `AddCoworkeeInfrastructure()` for Postgres, Redis and Mailpit |

## Example

```csharp
[DependsOn(typeof(CoworkeeApplicationModule))]
public sealed class OrdersModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context) =>
        context.Services.AddMessagingFromAssembly(typeof(OrdersModule).Assembly);
}

public sealed record PlaceOrder(string Product) : ICommand<Result<Guid>>;

internal sealed class PlaceOrderHandler(AppDbContext db) : IHandler<PlaceOrder, Result<Guid>>
{
    public Task<Result<Guid>> HandleAsync(PlaceOrder request, CancellationToken ct)
    {
        var order = Order.Place(request.Product);
        db.Orders.Add(order);
        return Task.FromResult<Result<Guid>>(order.Id);
    }
}
```

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddCoworkee<OrdersModule>();
var app = builder.Build();
app.UseCoworkee();
app.MapPost("/orders", (PlaceOrder cmd, IDispatcher d, CancellationToken ct) => d.SendAsync(cmd, ct).ToHttpResult());
app.Run();
```

The unit of work saves after a successful command, the audit trail records every changed field, and domain events leave through the outbox in the same transaction.

## Build

```bash
dotnet test --solution Coworkee.slnx   # needs Docker for the Postgres tests
pwsh build/pack-local.ps1              # packs into artifacts/nuget for local consumers
```

Packages are not on nuget.org yet. Consumers point a `nuget.config` source at `artifacts/nuget`.

## Used by

[Sharemee](https://github.com/fgilde/sharemee): assets, documents, knowledge base and intranet.

## License

MIT · built by [gilde.org](https://gilde.org)
