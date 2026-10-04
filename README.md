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
| `Coworkee.Contracts` | DTOs, permission names and paging records shared by server and clients |
| `Coworkee.Identity` | Users, roles, groups, tenants and permission grants with resource level checks |
| `Coworkee.AuthServer` / `Coworkee.Account` | OpenIddict server, login, registration, two factor and account pages |
| `Coworkee.Bff` | Backend for frontend: cookie session, token handling, API forwarding |
| `Coworkee.Settings` | Typed settings per global, tenant and user scope with encrypted secrets |
| `Coworkee.Mailing` | Scriban mail templates with overrides, queued SMTP delivery |
| `Coworkee.Notifications` / `Coworkee.Realtime` | In app notifications, digests and SignalR push |
| `Coworkee.BackgroundJobs` | Hangfire jobs with queues, retries and delayed follow-ups |
| `Coworkee.Storage` | Blob storage on the file system or Azure |
| `Coworkee.Search` / `Coworkee.Search.Elasticsearch` | Search index abstraction and Elasticsearch provider |
| `Coworkee.Auditing` | Audit trail queries and history views |
| `Coworkee.Theming` | Tenant themes and branding |
| `Coworkee.Ai` | Assistant chat with Claude over the tools of all modules, MCP server, tool call audit |
| `Coworkee.Client.Blazor` | MudBlazor shell, navigation, settings and admin pages for WebAssembly clients |

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

Until the packages are on nuget.org, consumers point a `nuget.config` source at `artifacts/nuget`.

## Release

Pushing a tag `v1.2.3` builds, tests and publishes all packages with that version to nuget.org (workflow `release.yml`, secret `NUGET_API_KEY`).

## Used by

[Sharemee](https://github.com/fgilde/sharemee): assets, documents, knowledge base and intranet.

## License

MIT · built by [gilde.org](https://gilde.org)
