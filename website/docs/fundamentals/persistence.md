# Persistence

The app has one `DbContext` derived from `CoworkeeDbContext`. Modules contribute their entities; Coworkee contributes identity, settings, audit and outbox tables.

```csharp title="MyApp.Infrastructure/MyAppDbContext.cs"
public sealed class MyAppDbContext(DbContextOptions<MyAppDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);
```

```csharp title="MyApp.Infrastructure/MyAppInfrastructureModule.cs"
context.Services.AddCoworkeeDbContext<MyAppDbContext>((provider, options) => options.UseNpgsql(
    provider.GetRequiredService<IConfiguration>().GetConnectionString("myapp"),
    npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "cw")));
```

Handlers inject `CoworkeeDbContext` and use `db.Set<T>()`. There are no repositories; EF Core already is one.

## Base classes and markers

| Type | Gives |
|---|---|
| `Entity` | `Id`, a version 7 GUID set on construction |
| `AuditedEntity` / `AuditedAggregateRoot` | `CreatedAt`, `CreatedBy`, `ModifiedAt`, `ModifiedBy`, filled on save |
| `AggregateRoot` | `Raise(IDomainEvent)` for the outbox |
| `IMultiTenant` | query filter on the current tenant, `TenantId` set on insert |
| `ISoftDelete` | `Remove` marks the row deleted; queries hide it |
| `IHasConcurrencyToken` | optimistic concurrency with a `Version` column |
| `IVersioned` | keeps a snapshot per revision for the version history view |
| `[Realtime(permission)]` | publishes inserts, updates and deletes to SignalR clients with the permission |
| `[NotAudited]`, `[Sensitive]` | leave a type or property out of the audit trail, or record a property masked |

## Audit trail

Every change to an audited entity is written field by field with user, time and old and new value. The admin UI shows it per entity (`AuditTimeline`) and globally (Audit log page).

## Tenants and the current user

`ICurrentUser` carries user id, tenant id and permissions of the request. Background jobs and the outbox restore the user who started them. To act as someone else, for example in a seed, open a scope:

```csharp
using (CurrentUserScope.Begin(new ImpersonatedUser(adminId, tenantId)))
{
    // queries and inserts run as this user and tenant
}
```

## Migrations and seeding

Migrations live in the infrastructure project:

```bash
dotnet ef migrations add AddOrders --project src/MyApp.Infrastructure
```

A small host applies them. The template's migration service also seeds:

```csharp title="MyApp.Migrations/Program.cs"
var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddCoworkeeModules<MyAppDatabaseModule>(builder.Configuration);
builder.Services.AddCoworkeeIdentitySeed(DemoSeed.Configure);
builder.Services.AddScoped<ISetupStep, DemoData>();
using var host = builder.Build();

await using var scope = host.Services.CreateAsyncScope();
await scope.ServiceProvider.GetRequiredService<MyAppDbContext>().Database.MigrateAsync();
await host.Services.SeedCoworkeeIdentityAsync();
```

`AddCoworkeeIdentitySeed` replaces the setup wizard: it creates the default tenant, the administrator, roles with their permission grants and further users, once. `ISetupStep` implementations run inside the same setup, as the administrator, and are the place for sample data. See [Authentication](../security/authentication.md#setup-wizard-or-seed).
