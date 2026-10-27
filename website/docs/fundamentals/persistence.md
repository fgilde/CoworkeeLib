# Persistence

The app has one `DbContext` derived from `CoworkeeDbContext`. Modules contribute their entities; Coworkee contributes identity, settings, audit and outbox tables.

```csharp title="MyApp.Infrastructure/MyAppDbContext.cs"
public sealed class MyAppDbContext(DbContextOptions<MyAppDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);
```

```csharp title="MyApp.Infrastructure/MyAppInfrastructureModule.cs"
context.Services.AddCoworkeeDbContext<MyAppDbContext>((provider, options) =>
    options.UseCoworkeeDatabase(provider.GetRequiredService<IConfiguration>(), "myapp"));
```

`UseCoworkeeDatabase` picks the EF Core provider from `Coworkee:Database:Provider` (see [Database providers](#database-providers)) and keeps the migration history in `cw.__EFMigrationsHistory`.

Handlers inject `CoworkeeDbContext` and use `db.Set<T>()`. There are no repositories; EF Core already is one.

## Database providers

| `Coworkee:Database:Provider` | EF Core provider | Aspire app host | Good for |
|---|---|---|---|
| `Postgres` (default) | Npgsql | `AddCoworkeeApp` starts a Postgres container | everything, production |
| `SqlServer` | Microsoft.EntityFrameworkCore.SqlServer | `options.UseSqlServer()`, a SQL Server container | organisations that run SQL Server or Azure SQL |
| `Sqlite` | Microsoft.EntityFrameworkCore.Sqlite | `options.UseSqlite()`, the file `.data/<name>.db` | demos, prototypes, one machine |

The app host sets the setting on every service, next to the connection string. Elsewhere (tests, your own hosting) set it like any other configuration value. `coworkee new --database sqlServer` writes the app host call and creates the initial migration for that provider.

The provider-specific parts sit behind small seams in `Coworkee.Infrastructure`, nothing to do in an app:

| Part | Postgres | SQL Server | SQLite |
|---|---|---|---|
| `IHasConcurrencyToken.Version` | `xmin` | column, counted up on every update | column, counted up on every update |
| `HasColumnType("jsonb")` | `jsonb` | `nvarchar(max)` | `TEXT` |
| Outbox batch | `FOR UPDATE SKIP LOCKED` | `UPDLOCK, READPAST` | plain query, no row locks |
| `IDistributedLock` | advisory lock | `sp_getapplock` | file lock in the temp folder (one machine) |
| Background jobs | Hangfire.PostgreSql | Hangfire.SqlServer | Hangfire.InMemory, every process runs its own job server |
| Text comparison | case-sensitive | case-sensitive (`Latin1_General_100_CS_AS` on every text column) | case-sensitive (`=`) |

Limits to know:

- **Migrations belong to one provider.** An app keeps the migrations of the provider it was created with. To switch, delete `Migrations` and run `coworkee migrations add Initial` again. To ship one app for several providers, follow the EF Core guide [Migrations with multiple providers](https://learn.microsoft.com/ef/core/managing-schemas/migrations/providers) with one migrations assembly per provider.
- **[Database backups](../modules/backups.md) need Postgres.** The template leaves the module out for the other providers.
- **SQLite** has one writer at a time, keeps background jobs in memory (queued and delayed jobs are lost on restart), stores `DateTimeOffset` as a number and `decimal` as `double`, does not enforce `HasMaxLength` and knows no schemas (tables lose their `cw.` prefix). It is meant for development and small single machine installations.
- Facets over `bool` columns (`[ProvideFacet]` on `IsActive`) fail on SQL Server and SQLite, which hand the value back as `1`/`0`; fixed in the next Nextended release.
- MySQL and MariaDB are not offered: Pomelo, the usual provider, has no release for EF Core 10 yet.

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

## External databases

An app can also read and write a database it does not own, for example another team's database with its own schema. Map it in a plain `DbContext` (not `CoworkeeDbContext`: that would add the library's tables and conventions), register it as usual and tell Coworkee about it:

```csharp title="MyApp.Shipping/ShippingModule.cs"
services.AddDbContextPool<HubDbContext>(options => options.UseSqlServer(configuration.GetConnectionString("Hub")));
services.AddCoworkeeExternalDbContext<HubDbContext>();
services.AddODataEntity<Shipment>("Shipments", ShippingPermissions.Shipments.View);
services.AddScoped<IODataEntityFilter<Shipment>, ShipmentsOfMyCustomers>();
```

- OData sets, row filters, global search, export, reports and client entities read each entity from the context that maps it.
- Handlers inject the external context and change it; the unit of work saves the external contexts with changes first, then the app's context. There is no transaction across the two databases: if the app's context fails, the external changes stay saved.
- Migrations, audit trail, history, soft delete, tenant filters, `[Realtime]`, webhooks, workflows and the outbox stay with the app's context. Rows of an external context have no tenant, so a row filter decides who sees what, and an external entity needs its OData set before attachments or comments may open its rows.
- One query cannot read two databases. A row filter on an external set that depends on the app's data (groups, customers) loads those ids first and passes them as a list: `var codes = await db.Set<OrgCode>().Where(...).Select(c => c.Code).ToListAsync(); return query.Where(s => EF.Parameter(codes).Contains(s.OrgCode));`. Child filters (`AddODataChildFilter`) need child and parent in the same database, and computed OData properties with `ComputedQueryRoot` work only on the app's context; [computed properties](../web/odata.md#computed-properties) over the row's own columns work on external sets too.
- Tags on external records compare their ids as one list, and attachments of external records are not cleaned up when the record disappears.
- Every save creates the registered external contexts to look for changes, so register only contexts the app writes or reads through Coworkee.
- Client entities need the dates the external table keeps (`ChangedAt` in the [settings](../ui/client-entities.md#step-2-settings-optional)); without them a set downloads completely on every refresh. Raise `Overlap` when another system writes the dates with its own clock or commits long after it stamps them.

## Migrations and seeding

Migrations live in the infrastructure project and are made for its database provider:

```bash
dotnet ef migrations add AddOrders --project src/MyApp.Infrastructure
```

A small host applies them. The template's migration service also seeds:

```csharp title="MyApp.Migrations/Program.cs"
var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddCoworkeeModules<MyAppDatabaseModule>(builder.Configuration);
builder.AddCoworkeeMigrations<MyAppDbContext>(options =>
{
    options.DevelopmentSeed = DemoSeed.Configure;
    options.DeploymentSeed = DemoSeed.ConfigureDeployment;
});
builder.Services.AddScoped<ISetupStep, DemoData>();
using var host = builder.Build();
return await host.RunCoworkeeMigrationsAsync();
```

`AddCoworkeeMigrations` (`Coworkee.Identity.Setup`) plans the run and `RunCoworkeeMigrationsAsync` carries it out, with exit code 0 or 1:

1. migrate the database, retried every 3 seconds while its server does not accept connections yet (`Migrations:Attempts`, default 20, or `options.Attempts`);
2. seed: with a first administrator from the deploy parameters (`Coworkee:InitialAdmin`, see [Setup wizard or seed](../security/authentication.md#setup-wizard-or-seed)) `DeploymentSeed` with it; otherwise `DevelopmentSeed` in the Development environment (`options.DevelopmentUsers` decides elsewhere), followed by `SyncDevelopmentUsersAsync`; otherwise nothing, and the setup page with its token sets the system up;
3. bring a deployed Keycloak in line (`ReconcileKeycloakRealmAsync`).

It also builds the container with the production defaults: a local run is Development, whose scope checks fail on the MVC services of OData in a host without a web environment.

`AddCoworkeeIdentitySeed` replaces the setup wizard: it creates the default tenant, the administrator, roles with their permission grants, further users and system wide settings (`seed.Settings["Theme.Mode"] = "dark"`), once. `ISetupStep` implementations run inside the same setup, as the administrator, and are the place for sample data. `SyncDevelopmentUsersAsync` runs in the Development environment only (or with `Coworkee:Identity:SyncDevelopmentUsers`, which the Aspire app host sets for the migrations of a local run) and keeps an existing database in step with the seed: users and roles it lacks are added, seed users get the roles and seeded roles the permissions added to the seed since, seed users whose password differs get the seeded one again; other users, roles and grants stay as they are, nothing is removed. See [Authentication](../security/authentication.md#setup-wizard-or-seed).
