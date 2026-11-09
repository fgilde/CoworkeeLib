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

An interceptor that adds rows while saving registers as `IPreparingInterceptor` (`services.AddScoped<IPreparingInterceptor, MyDefaults>()`) instead of `IInterceptor`: it runs before the built-in ones, so its rows get tenant and audit fields, audit entries, history, outbox, realtime and webhook messages. The identity module adds a new user's default roles and default group this way.

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
using var host = builder.Build();
return await host.RunCoworkeeMigrationsAsync();
```

`AddCoworkeeMigrations` (`Coworkee.Identity.Setup`) plans the run and `RunCoworkeeMigrationsAsync` carries it out, with exit code 0 or 1:

1. migrate the database, retried every 3 seconds while its server does not accept connections yet (`Migrations:Attempts`, default 20, or `options.Attempts`), and give groups without a `Code` path theirs;
2. seed: with a first administrator from the deploy parameters (`Coworkee:InitialAdmin`, see [Setup wizard or seed](../security/authentication.md#setup-wizard-or-seed)) `DeploymentSeed` with it; otherwise `DevelopmentSeed` in the Development environment (`options.DevelopmentUsers` decides elsewhere), followed by `SyncDevelopmentUsersAsync`; otherwise nothing, and the setup page with its token sets the system up;
3. run the [seed contributors](#seed-contributors) of the app and its modules, also when step 2 did nothing;
4. run `options.AfterSeed`, if set (prefer seed contributors);
5. bring a deployed Keycloak in line (`ReconcileKeycloakRealmAsync`);
6. drop the cached permissions, settings and features of running services (with Redis on every instance), so a re-run against a running app or a rolling deployment takes effect at once.

It also builds the container with the production defaults: a local run is Development, whose scope checks fail on the MVC services of OData in a host without a web environment.

`AddCoworkeeIdentitySeed` replaces the setup wizard: it creates the default tenant, the administrator, roles with their permission grants, further users and system wide settings (`seed.Settings["Theme.Mode"] = "dark"`), once. `ISetupStep` implementations run inside the same setup (here or in the setup wizard), as the administrator; for app data prefer seed contributors. `SyncDevelopmentUsersAsync` runs in the Development environment only (or with `Coworkee:Identity:SyncDevelopmentUsers`, which the Aspire app host sets for the migrations of a local run) and keeps an existing database in step with the seed: users and roles it lacks are added, seed users get the roles and seeded roles the permissions added to the seed since, seed users whose password differs get the seeded one again; other users, roles and grants stay as they are, nothing is removed. See [Authentication](../security/authentication.md#setup-wizard-or-seed).

`options.SyncRolesInDeployments = true` does the roles part of that for deployments: on every run the roles of `DeploymentSeed` and their grants reach the default tenant, also of a database set up earlier or in the browser (`DeploymentSeed` is then called with a placeholder administrator, for its roles only). Missing roles and grants are added, nothing is removed, users and passwords stay untouched. Off by default; it runs before the other seed contributors.

### Seed contributors

Data every database needs (reference data, tags, settings of a module) and edits of the seeded accounts go into an `ISeedContributor` (`Coworkee.Application.Setup`), a class with injected services like any other:

```csharp title="MyApp.Sales/Seeds/StandardTagsSeed.cs"
[RegisterAs(typeof(ISeedContributor))]
internal sealed class StandardTagsSeed(SalesDbContext db, IDispatcher dispatcher) : ISeedContributor
{
    private static readonly string[] Tags = ["Urgent", "Customs hold"];

    public async Task SeedAsync(SeedContext context, CancellationToken cancellationToken)
    {
        var existing = await db.Tags.Select(t => t.Name).ToListAsync(cancellationToken);
        foreach (var name in Tags.Except(existing))
        {
            await dispatcher.SendAsync(new CreateTag(name), cancellationToken).OrThrow();
        }
    }
}
```

`coworkee seed add StandardTags --module Sales` writes such a class (see [CLI](../getting-started/cli.md)). `[RegisterAs]` works in the modules and in the migrations project (`AddCoworkeeMigrations` scans it too); `services.AddTransient<ISeedContributor, StandardTagsSeed>()` works anywhere.

| Member | Default | |
|---|---|---|
| `SeedAsync(SeedContext, CancellationToken)` | | `context.TenantId` (null for `Host`), `context.IsDevelopment`, `context.IsNew` |
| `Order` | 0 | lower runs first; ties by type name. Each contributor runs for every tenant before the next one starts |
| `Environments` | `Always` | `Development` (a run that seeds the demo users; also without a development seed when the Development environment or `options.DevelopmentUsers` says so) or `Deployment` |
| `Target` | `EachTenant` | once per tenant with the tenant set (inactive ones too), or `Host`: once per run without a tenant |
| `Required` | true | false: a failure is logged as a warning and the run goes on |

How a call runs: in a DI scope and a database transaction of its own, as the system with every permission (`SystemActor`), in the tenant of the call. Requests with `[RequiresPermission]` pass, `IMultiTenant` rows get the tenant, the audit trail shows "system", `CreatedBy` stays empty. `UserManager`, the dispatcher's commands and what the contributor leaves unsaved are written in the transaction; the runner saves at the end. A seed that wants a person as owner opens `CurrentUserScope.Begin(new ImpersonatedUser(userId, tenantId))` around that part. The system is a member of no group: pass group ids explicitly or read with `IgnoreQueryFilters()`. Other databases (external DbContexts) and blob storage are not in the transaction; writes there must be idempotent on their own.

**Every run, so idempotent.** A contributor runs on every migrations run: read what exists, then add or change only what is missing. Changes to a seed (a new tag, a new grant) reach every database with the next run. What users own (sample records) belongs under `if (!context.IsNew) return;`: `IsNew` is true in the run whose identity seed set the system up and for a tenant just set up, so such records do not come back after a delete. There is no record of which seeds ran.

**Errors.** A failing required contributor rolls its transaction back, the log names contributor and tenant, and the run stops with exit code 1, so the services of a deployment do not start against a half-seeded database. Contributors after it do not run, since they may build on it.

**Where they run.**

| Contributor | Migrations run (every run) | Setup wizard, new tenant (`POST /api/v1/tenants`) |
|---|---|---|
| in a module, `EachTenant` | for every tenant | in the api, for the new tenant, in the background after the commit (`IsNew` true) |
| in a module, `Host` | once | no |
| in the migrations project | yes | no, the api does not load that project |
| `SyncRolesInDeployments` | deployments | no |

The api runs them for a tenant set up at run time through the outbox (`TenantSeedRequested`, also raised by `tenant.RequestSeed()`): the outbox processor of the api starts them once the setup or the new tenant is committed, with `IsDevelopment` from the Development environment of the api. A failure there is logged as an error and not retried (a retry would repeat what `IsNew` guards); the next migrations run (a deployment, *Re-run migrations* in the Aspire dashboard, `coworkee db seed`) is the fallback for everything but `IsNew`. Contributors in the migrations project reach such tenants only with that next run.

Writes of a seed send realtime messages and outbox events like any save: the outbox processor of the api delivers them once it runs. Two migrations runs at the same time (an overlapping job retry) run every seed twice; idempotent seeds and unique indexes make the second one a no-op or a rolled-back failure.

**Testing.** `services.RunSeedContributorAsync<T>(new SeedContext(tenantId, IsDevelopment: true, IsNew: true))` runs one contributor the way the migrations do, also one the host does not register (see [Testing](../testing.md)).
