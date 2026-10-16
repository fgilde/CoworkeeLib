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
builder.Services.AddCoworkeeIdentitySeed(DemoSeed.Configure);
builder.Services.AddScoped<ISetupStep, DemoData>();
using var host = builder.Build();

await using var scope = host.Services.CreateAsyncScope();
await scope.ServiceProvider.GetRequiredService<MyAppDbContext>().Database.MigrateAsync();
await host.Services.SeedCoworkeeIdentityAsync();
await host.Services.SyncDevelopmentUsersAsync();
```

`AddCoworkeeIdentitySeed` replaces the setup wizard: it creates the default tenant, the administrator, roles with their permission grants, further users and system wide settings (`seed.Settings["Theme.Mode"] = "dark"`), once. `ISetupStep` implementations run inside the same setup, as the administrator, and are the place for sample data. `SyncDevelopmentUsersAsync` runs in the Development environment only (or with `Coworkee:Identity:SyncDevelopmentUsers`, which the Aspire app host sets for the migrations of a local run) and keeps an existing database in step with the seed: users and roles it lacks are added, seeded roles get the permissions added to the seed since, seed users whose password differs get the seeded one again; other users, roles and grants stay as they are, nothing is removed. See [Authentication](../security/authentication.md#setup-wizard-or-seed).
