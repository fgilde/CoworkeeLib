# Seed contributors

Driver: the owner's question (2026-11-09): should the library have something like ABP's data seed contributors, so apps write their own seeds that run cleanly with the migrator (tags in CoShipee, editing users, ...) and get real services there (`UserManager`, the dispatcher)? The migration may change, also to a new system, but it must stay backwards compatible: the demo users must keep working as they do today.

Short answer: **yes, as a thin layer on what exists.** One interface, `ISeedContributor`, run by the migrations process after the identity seed, each call in its own DI scope and transaction, as the system with every permission, once per tenant or once per run. Every contributor runs on every migrations run and is idempotent. Nothing that exists today changes; `DevelopmentSeed`, `DeploymentSeed`, `SyncDevelopmentUsersAsync`, `ISetupStep` and `AfterSeed` keep their meaning and order.

## 1. How seeding works today

### The migrations process

`templates/Coworkee.Templates/content/CoworkeeApp/src/MyApp.Migrations/Program.cs` (CoShipee: `C:\dev\privat\github\CoShipee\src\CoShipee.Migrations\Program.cs`, identical apart from `DemoData`):

```csharp
builder.Services.AddCoworkeeModules<MyAppDatabaseModule>(builder.Configuration);
builder.AddCoworkeeMigrations<MyAppDbContext>(options =>
{
    options.DevelopmentSeed = DemoSeed.Configure;
    options.DeploymentSeed = DemoSeed.ConfigureDeployment;
});
builder.Services.AddScoped<ISetupStep, DemoData>();   // only with --samples
using var host = builder.Build();
return await host.RunCoworkeeMigrationsAsync();
```

`src/Coworkee.Identity/Setup/CoworkeeMigrations.cs` plans and runs (the host is built, never started, so hosted services of the modules do not run here):

| Step | What | Condition |
|---|---|---|
| 1 | `Database.MigrateAsync` with retries (`Migrations:Attempts`, 3 s apart), `GroupCodes.FillMissingAsync` | always |
| 2 | `SeedCoworkeeIdentityAsync` → `IdentitySeeder.SeedAsync` (`IdentitySeeder.cs`): default tenant, system roles, setup admin, `ISetupStep`s, seed roles with grants, further users, default group, global settings | a first administrator from `Coworkee:InitialAdmin` (→ `DeploymentSeed`, default: the admin alone), or a development run (→ `DevelopmentSeed`). Does nothing when `SystemState.IsInitialized` |
| 3 | `SyncDevelopmentUsersAsync` → `IdentitySeeder.SyncAsync`: missing seed users and roles, roles added to seed users, permissions added to seeded roles, seeded passwords again; nothing removed | development run, and `IsDevelopment()` or `Coworkee:Identity:SyncDevelopmentUsers` (the Aspire app host sets it, `CoworkeeApp.cs` `AddMigrations`) |
| 4 | `options.AfterSeed(IServiceProvider, ct)`: "the app's own data steps" | when set |
| 5 | `ReconcileKeycloakRealmAsync` (`KeycloakRealmSetup.cs`): clients and the configured users of the realm | Keycloak configured |

A development run is `InitialAdmin is null && DevelopmentSeed is not null && (options.DevelopmentUsers ?? IsDevelopment())`. Exit code 0, or 1 with a critical log on any exception (`CoworkeeMigrationsOptions.cs` documents the options).

### Other places that seed

| What | Where | Process, when |
|---|---|---|
| `ISetupStep` (`src/Coworkee.Application/Setup/ISetupStep.cs`): global settings (`Coworkee.Settings/SettingsSetupStep.cs`), built-in themes (`Coworkee.Theming/ThemeSetupStep.cs`), configured languages (`Coworkee.Localization/Languages/LanguageSetupStep.cs`), the template's sample catalog (`MyApp.Migrations/DemoData.cs`) | `SystemInitializer.InitializeAsync`, as the setup admin, before the roles and users of the seed | once: in the migrations process when the identity seed sets the system up, or in the **API** when the setup wizard completes (`CompleteSetupHandler`). `DemoData` is registered only in the migrations project, so a wizard setup never gets it; with `--samples` a deployment with deploy parameters does get it |
| Built-in themes | `Coworkee.Theming/ThemeSeeds.EnsureAsync` (reconcile, process-wide semaphore) | lazily in the API on theme reads |
| OIDC clients and API scopes | `Coworkee.AuthServer/AuthClientSeeder.cs` (`IHostedService`, reconcile, removes stale managed ones) | auth server, every start |
| A new tenant's admin | `Coworkee.Features/TenantHandlers.cs` `CreateTenant` | API, at run time; nothing else is seeded for the tenant (no roles, no data) |
| Keycloak realm | `KeycloakRealmSetup` | migrations, every run |

In CoShipee (`src/CoShipee.Migrations/DemoSeed.cs`) `ConfigureSystem` adds the hub roles (`HubRoles()`), the default group and settings in **both** environments, mirroring ABP's `RolesDataSeedContributor`. Note a gap this exposes: the deployment seed runs only on an empty database, and `SyncAsync` only in development runs, so **a deployed CoShipee never gets hub roles or grants added later**. Seed contributors fix that class of problem (section 3.8).

### How the migrations run in deployments

`src/Coworkee.Aspire/CoworkeeApp.cs` `AddMigrations`, `CoworkeeApp.Deploy.cs`, `website/docs/hosting/deployment.md`:

- **Local (Aspire):** a project resource; api, auth and workers `WaitForCompletion(migrations)`. Dashboard commands *Re-run migrations* and *Reset database* (`CoworkeeApp.Dashboard.cs`); `coworkee db seed` (`src/Coworkee.Cli/Commands/DbCommand.cs`) runs the migrations project in Development against the running database.
- **Docker Compose:** a service that runs once before the others, on every `docker compose up`.
- **Azure Container Apps:** a Container Apps job (`PublishAsAzureContainerAppJob`), started by hand after every deployment (`az containerapp job start`).
- **Kubernetes:** a Helm hook Job after each install and upgrade.

So the migrations process runs on every deployment, alone, before the services start (apart from the Azure job, which is started after the new revision). That is the right home for seeds: one run per deployment, no replica races, the deployment fails visibly when a seed fails.

## 2. ABP's model, and what to keep

ABP: `IDataSeedContributor.SeedAsync(DataSeedContext context)`; `IDataSeeder.SeedAsync(context)` resolves all contributors (registered by convention, transient) in one DI scope and calls them in module order inside a unit of work. `DataSeedContext` has `TenantId` and a string-keyed `Properties` bag (`"AdminEmail"`, `"AdminPassword"`). The DbMigrator calls the seeder for the host and then each tenant (`CurrentTenant.Change(tenantId)`); the SaaS module calls it again when a tenant is created. Contributors inject anything: repositories, `IdentityUserManager`, `IPermissionDataSeeder`.

Good, and kept: plain constructor injection of real services; host vs. tenant runs with the tenant switched; one obvious place to put a seed.

Weaknesses, avoided:
- **No order** apart from module dependency order; apps depend on registration accidents.
- **No environment:** demo vs. production is an `if` in each contributor.
- **No record of what ran:** every contributor guards itself (`if (await repo.GetCountAsync() > 0) return;`), so a changed seed never reaches existing databases unless written as reconcile.
- **One unit of work** for all contributors (newer versions add `SeedInSeparateUowAsync`): one failure rolls back everything, or partial state when contributors save themselves.
- **Stringly typed properties** for passwords and options.
- **Per-tenant loops on every run**, with no way to scope a contributor to host or tenant except checking `context.TenantId`.
- **Two execution contexts** (migrator, tenant creation in the web app) with different services and users; contributors break in one of them.
- Permission checks are not applied (repositories), but application services called from a seed are; the seed runs as nobody and fails them.

## 3. Design

### 3.1 Goals

- An app or module writes a seed as a class with injected services (`UserManager<User>`, `CoworkeeDbContext`, the app's DbContext, `IDispatcher`, `SettingWriter`, `IGroupScope`, ...), registered with `[RegisterAs]` like everything else.
- It runs in the migrations process, after the schema and the identity seed, in Development and/or deployments, for each tenant or once.
- It can call the app's own commands through `IDispatcher` (validation, rules, audit) without failing permission checks.
- A failing seed fails the run (exit 1) with its name and tenant in the log, and leaves nothing half written in the app database.
- Existing apps behave exactly as before, with or without contributors.

### 3.2 Non-goals

- No seed history table in the first version (see 3.6 and open question 1).
- Seeds do not run in the API, auth server or workers; the setup wizard and `CreateTenant` do not trigger them (open question 2).
- No per-contributor timeouts, no dry run, no "run only this contributor" switch (later, if asked).
- Users created by a contributor are not mirrored to Keycloak (the realm takes its users from configuration).
- No change to `ISetupStep`, `AuthClientSeeder`, `ThemeSeeds` or EF `HasData`.

### 3.3 The contract

In `Coworkee.Application` (namespace `Coworkee.Application.Setup`, next to `ISetupStep`), so a module implements it without referencing `Coworkee.Identity`:

```csharp
namespace Coworkee.Application.Setup;

/// <summary>
/// Data the app keeps in its database: runs in the migrations process on every run, after the schema and the identity seed, in a DI scope and a
/// transaction of its own, as the system with every permission. Must be idempotent: check, then add or change what is missing.
/// Register with [RegisterAs(typeof(ISeedContributor))] in a module or the migrations project, or with AddSeedContributor&lt;T&gt;().
/// </summary>
public interface ISeedContributor
{
    Task SeedAsync(SeedContext context, CancellationToken cancellationToken);

    /// <summary>Lower runs first; ties by type name. The identity seed is <see cref="SeedOrder.Identity"/>, the development users <see cref="SeedOrder.DevelopmentUsers"/>.</summary>
    int Order => 0;

    /// <summary>Development runs (the demo users are seeded), deployments, or both.</summary>
    SeedEnvironments Environments => SeedEnvironments.Always;

    /// <summary>Once per tenant with the tenant set (default), or once per run without one.</summary>
    SeedTarget Target => SeedTarget.EachTenant;
}

[Flags]
public enum SeedEnvironments { Development = 1, Deployment = 2, Always = Development | Deployment }

public enum SeedTarget { EachTenant, Host }

public static class SeedOrder
{
    public const int Identity = -2000;
    public const int DevelopmentUsers = -1000;
}

public sealed record SeedContext(
    Guid? TenantId,        // the tenant of this call; null for SeedTarget.Host
    bool IsDevelopment,    // a development run: the demo users are seeded
    bool IsNewDatabase);   // the identity seed set the system up in this run (the database had no system before)
```

Why default interface members and not attributes: they are visible in the class, testable, need no reflection, and a later member (`Version`, `Timeout`) is added without breaking implementations.

Why `EachTenant` is the default: entities implement `IMultiTenant`, and with the tenant set the query filters and `MultiTenantInterceptor` do the right thing without the contributor thinking about it. `Host` is for global data (languages, an external database such as CoShipee's hub, a global setting); there the current tenant is null and a contributor that writes `IMultiTenant` rows sets `TenantId` itself.

`IsDevelopment` follows the demo users, not the host environment name alone: `InitialAdmin is null && (options.DevelopmentUsers ?? IsDevelopment())`. Unlike today's development-run flag it does not require `DevelopmentSeed`, so an app without demo users still gets its Development contributors locally. A deployment (deploy parameters present, or Production with the setup token) is `Deployment`.

### 3.4 Registration

```csharp
// in a module assembly: AddCoworkeeModules scans it already
[RegisterAs(typeof(ISeedContributor))]
internal sealed class StandardTagsSeed(...) : ISeedContributor { ... }

// explicit, e.g. in the migrations project
builder.Services.AddSeedContributor<DemoProfilesSeed>();
```

```csharp
public static IServiceCollection AddSeedContributor<T>(this IServiceCollection services) where T : class, ISeedContributor
{
    services.TryAddEnumerable(ServiceDescriptor.Transient<ISeedContributor, T>());
    return services;
}
```

`AddCoworkeeModules` scans only module assemblies (`src/Coworkee.Core/Modularity/ModuleServiceCollectionExtensions.cs`), not the migrations project. `AddCoworkeeMigrations` therefore also calls `services.AddRegisterAsServices(Assembly.GetEntryAssembly()!)`, so `[RegisterAs]` works in the migrations project too. The existing de-duplication there keeps double registrations out.

Contributors in a module are registered in the API and workers as well. That is harmless: only the migrations runner resolves `IEnumerable<ISeedContributor>`. Where to put a seed: data a module owns (tags of the hub, default document types) in the module; demo data and app-specific user edits in the migrations project next to `DemoSeed`.

### 3.5 Where and how they run

`RunCoworkeeMigrationsAsync` gains one step; everything else stays in place:

| Order | Step | Change |
|---|---|---|
| 1 | migrate, `GroupCodes` | none |
| 2 | identity seed (`DeploymentSeed` / `DevelopmentSeed`, with `ISetupStep`s) | none; listed as built-in contributor at `SeedOrder.Identity` |
| 3 | `SyncDevelopmentUsersAsync` | none; built-in at `SeedOrder.DevelopmentUsers` |
| 4 | **app and module contributors**, by `Order` then type name | new; also runs when step 2 does nothing (setup token deployments, already set up databases) |
| 5 | `AfterSeed` | none; still after all seeds |
| 6 | Keycloak reconcile | none |

The two built-ins are internal `ISeedContributor`s that call exactly what runs today, with today's conditions, scope and actor, and **without** the new transaction wrapping (zero behaviour change). They exist in the list so the log, the order numbers and the test helper see one sequence. A contributor with `Order < SeedOrder.Identity` runs before the identity seed; on a fresh database it then sees no tenant yet, which the docs say.

The runner (`Coworkee.Identity.Setup.SeedRunner`, internal):

```text
isNew = !SystemState.IsInitialized            (read before step 2)
for each contributor type, ordered by Order, then FullName:
    skip when its Environments do not include this run (Debug log)
    targets = Host ? [null] : every tenant id (IgnoreQueryFilters, also inactive tenants; in the order of creation)
    for each tenant in targets:
        using actor = CurrentUserScope.BeginSystem(tenant, allPermissionNames)
        await using scope = services.CreateAsyncScope()
        db = scope's app DbContext
        strategy.ExecuteAsync:                       (provider retry strategy)
            begin transaction on db
            contributor = scope.GetRequiredService(type)
            await contributor.SeedAsync(new SeedContext(tenant, isDevelopment, isNew), ct)
            await db.SaveChangesAsync(ct)            (what the contributor left pending)
            commit
        log Information "Seed {Name} (tenant {Tenant}) in {Elapsed} ms"
```

Order first, tenants inside: contributor B can rely on A having run for every tenant.

**DI and the unit of work.** A scope per call keeps the change tracker small and isolates failures. `UserManager`, `IDispatcher` commands (the `UnitOfWorkMiddleware` saves) and the runner's final save all write through the scope's `CoworkeeDbContext` and so join its transaction. Contributors may save themselves; they need not. Other DbContexts (CoShipee's `HubDbContext`) are not in the transaction; a contributor that writes them saves them itself, and makes that write idempotent, since a rollback of the app database does not undo it. Files in blob storage likewise.

**Actor and permissions.** `SystemActor` (`src/Coworkee.Core/Security/SystemActor.cs`) with every defined permission (`IPermissionDefinitionManager.All`): requests with `[RequiresPermission]` pass, audit entries show "system", `CreatedBy` is null, and the existing system exceptions apply (`AccountGuard.IsSystem`, `AdminReach` skips group-scoped administration). A seed that wants a person as creator (the template's report needs `OwnerId`) opens `CurrentUserScope.Begin(new ImpersonatedUser(adminId, tenantId))` around that part, as `IdentitySeeder` does. The `SystemActor` is a member of no group: app filters built on `IGroupScope.ActiveAsync` see nothing, so seeds pass group ids explicitly (as `CreateTag(name, color, groupId)` does) or read with `IgnoreQueryFilters()`.

**Errors.** Fail fast: an exception (or a failed `Result` turned into one with the existing `.OrThrow()`) rolls the call's transaction back, is logged critical with contributor and tenant, and ends the run with exit code 1, as a failed identity seed does today. The services then do not start against a half-seeded database (Compose and Aspire wait for successful completion). Later contributors may depend on earlier ones, which is why the run does not continue. A seed whose failure should not block a deployment catches its own exceptions and logs.

**Logging and tracing.** One `ActivitySource("Coworkee.Seed")` span per call (contributor, tenant), so the run shows in the Aspire dashboard and Grafana like the other services.

**Timeouts.** None per contributor. The host's cancellation (SIGTERM, Ctrl+C) reaches every call; the job runtime (Container Apps job timeout, Helm hook timeout) bounds the whole run. `ponytail:` add `Seed:Timeout` when a hanging seed is ever seen.

**Concurrency.** The migrations job is a single instance by design. EF's migration lock covers step 1 only; two runs at the same time (a Kubernetes job retry overlapping) would run seeds twice. Idempotent seeds plus unique indexes make the second one a no-op or a rolled-back failure. The docs say so.

### 3.6 Seed state: every run or once?

| | Every run, idempotent (recommended) | Once, with a history table |
|---|---|---|
| Model | ABP, `IdentitySeeder.SyncAsync`, `ThemeSeeds.EnsureAsync`, `AuthClientSeeder`, `LanguageSetupStep` | EF migrations |
| Changed seed (new tag, new role grant) | reaches every database on the next run | needs a version bump, or a hash |
| Schema | none | `cw.SeedHistory (Name, TenantId, Version, AppliedAt)`: a library table, so **every app needs a migration** after the upgrade, and `Migrations.Tests` fails until it has one |
| Deleted by a user (a seeded tag removed in the UI) | comes back on the next run | stays deleted |
| Cost | each contributor checks on each run (cheap for tens of rows; per-tenant loops scale with tenants) | one lookup |
| Authoring | the author writes the check | the author may skip the check, until the first rerun after a crash between seed and history write |

Recommendation: **every run, idempotent**, for the first version. It matches every seed the library already has, needs no schema change (no forced migration for existing apps), and makes changed seeds reach deployed databases, which is the gap CoShipee has today. The "comes back after delete" behaviour is the one real cost; the guidance is "seed what must exist (roles, grants, reference data); seed what users own (sample records) only into a new database": `if (!context.IsNewDatabase) return;` gives ISetupStep-like once semantics without a table.

When a true once is needed (a one-time import, a data fix that is not idempotent, a welcome mail), phase 2 adds it without breaking anything:

```csharp
/// <summary>null (default): every run. Set: runs once per tenant until this value changes; recorded in cw.SeedHistory.</summary>
string? Version => null;
```

A declared version, not a code hash: hashes of IL or data change with compiler or formatting changes and re-run seeds nobody touched. The history row is written in the call's transaction, keyed `(Name, TenantId)` with `Guid.Empty` for host runs, so a crash re-runs the seed, and a concurrent second run fails on the key and rolls back.

### 3.7 Side effects

- **Audit trail:** written by the interceptors as for any save, actor "system". Wanted: a seed's changes are traceable.
- **Outbox / integration events:** persisted in the same transaction; the migrations host is never started, so the API's or worker's outbox processor delivers them once the services run. Nothing to do.
- **Realtime:** `RealtimeChangeInterceptor` sends after commit from the migrations process. On a first run no client is connected; on a re-run against a running app (dashboard *Re-run migrations*, a deployment with rolling services) messages only reach clients when the migrations process has the realtime module and the Redis backplane. The design adds nothing: seeds are not the place to promise live updates, and the same is true today for `SyncDevelopmentUsersAsync`. Open question 5 covers caches.
- **Domain event handlers** run as in a request (in-process, same scope).

### 3.8 CoShipee example

Standard tags of the hub, in the module (`CoShipee.Hub/Seeds/StandardTagsSeed.cs`), every environment, per tenant:

```csharp
[RegisterAs(typeof(ISeedContributor))]
internal sealed class StandardTagsSeed(CoworkeeDbContext db, HubDbContext hub, IDispatcher dispatcher) : ISeedContributor
{
    private static readonly (string Name, string Color)[] Tags = [("Urgent", "#d32f2f"), ("Customs hold", "#f57c00"), ("Hazardous goods", "#7b1fa2")];

    public async Task SeedAsync(SeedContext context, CancellationToken cancellationToken)
    {
        // illustrative: the tenant's first root group that is not the default one (tags there are shared, in the default group private)
        var root = await db.Set<UserGroup>().Where(g => g.ParentId == null && !g.IsDefault).OrderBy(g => g.Name).Select(g => (Guid?)g.Id).FirstOrDefaultAsync(cancellationToken);
        if (root is null)
        {
            return;
        }

        var existing = await hub.Tags.Where(t => t.OrganizationUnitId == root).Select(t => t.Name).ToListAsync(cancellationToken);
        foreach (var (name, color) in Tags.Where(t => !existing.Contains(t.Name)))
        {
            await dispatcher.SendAsync(new CreateTag(name, color, root), cancellationToken).OrThrow();
        }
    }
}
```

Editing users, development only, in the migrations project (`CoShipee.Migrations/Seeds/DemoAccountsSeed.cs`), after the demo users exist:

```csharp
[RegisterAs(typeof(ISeedContributor))]
internal sealed class DemoAccountsSeed(UserManager<User> users, CoworkeeDbContext db, IDispatcher dispatcher) : ISeedContributor
{
    public SeedEnvironments Environments => SeedEnvironments.Development;

    public async Task SeedAsync(SeedContext context, CancellationToken cancellationToken)
    {
        // a locked demo account to try the unlock flow with
        if (await users.FindByEmailAsync("locked@coshipee.local") is { } locked && !await users.IsLockedOutAsync(locked))
        {
            await dispatcher.SendAsync(new LockUser(locked.Id, null), cancellationToken).OrThrow();
        }

        // the dispatcher demo user in the Hamburg group
        var hamburg = await db.Set<UserGroup>().Where(g => g.Name == "Hamburg").Select(g => (Guid?)g.Id).FirstOrDefaultAsync(cancellationToken);
        if (hamburg is { } group && await users.FindByEmailAsync("dispatcher@coshipee.local") is { } user
            && !await db.Set<UserGroupMember>().AnyAsync(m => m.UserId == user.Id && m.GroupId == group, cancellationToken))
        {
            var groups = await db.Set<UserGroupMember>().Where(m => m.UserId == user.Id).Select(m => m.GroupId).ToListAsync(cancellationToken);
            await dispatcher.SendAsync(new SetUserGroups(user.Id, [.. groups, group]), cancellationToken).OrThrow();
        }
    }
}
```

Guidance shown by the example: read with `UserManager` or the DbContext, change through the library's commands (`LockUser`, `SetUserGroups`, `SetUserRoles`, `UpdateUser`) so their rules (account guard, session stamps, audit) apply; `UserManager` directly for passwords and claims.

And the deployment gap from section 1: the hub roles in `DemoSeed.ConfigureSystem` reach deployed databases by a contributor that reconciles them (`Target = Host` is wrong here, roles are per tenant; `EachTenant`, `Always`). That needs a public reconcile for seed roles; see open question 3.

### 3.9 CLI, MCP, Studio

- `coworkee db seed` stays as it is: it runs the migrations project in Development, which now includes the contributors. Its description becomes "migrations, demo users and roles, and the seed contributors".
- New `coworkee seed add <Name> [--module <Module>] [--host] [--development | --deployment]`: writes `src/<App>.<Module>/Seeds/<Name>Seed.cs` (or `src/<App>.Migrations/Seeds/` without `--module`) with the `[RegisterAs]` attribute, the chosen members and an idempotency comment. Top-level `seed add` like `module add`, `entity add`, `migrations add`; not `db seed add`, because `db seed` is an action. MCP tool `add_seed` with the same arguments, docs search finds the page.
- Studio: nothing. It creates new apps; its *Users & roles* page already writes `DemoUsers.cs`. Seed data in app specs is a separate idea, not part of this.
- Template `AGENTS.md`: a line under *Required patterns*: "Data the app needs in every database: an `ISeedContributor` (idempotent), not code in `Program.cs` of the migrations project or `AfterSeed`."

### 3.10 Testing

Public helper in `Coworkee.Identity.Setup` (used by tests, and by apps that want to run one seed elsewhere):

```csharp
/// <summary>Runs one contributor the way the migrations do: own scope and transaction, as the system, for the tenant (or host).</summary>
public static Task RunSeedContributorAsync<T>(this IServiceProvider services, Guid? tenantId, bool development = true, CancellationToken cancellationToken = default)
    where T : class, ISeedContributor;
```

It creates the contributor with `ActivatorUtilities`, so a test in `Api.Tests` can run a contributor of the migrations project that the API does not register. Test pattern in the template's `Api.Tests`:

```csharp
[Fact]
public async Task Standard_tags_are_seeded_once()
{
    var setup = await fixture.SetupAsync();
    await fixture.Factory.Services.RunSeedContributorAsync<StandardTagsSeed>(setup.TenantId);
    await fixture.Factory.Services.RunSeedContributorAsync<StandardTagsSeed>(setup.TenantId);   // idempotent
    (await CountTagsAsync()).ShouldBe(3);
}
```

Library tests (`tests/Coworkee.Identity.Tests` or the integration suite): order by `Order` then name; environment filter; one call per tenant with the tenant set; transaction rollback on exception and exit code 1; built-ins unchanged (demo users and passwords as before, deployment admin as before); `[RegisterAs]` in the entry assembly picked up; an existing app without contributors runs identically (the template's `MigrationProcessTests` and app host tests).

### 3.11 Docs (EN and DE each)

| Page | Change |
|---|---|
| `website/docs/fundamentals/persistence.md` *Migrations and seeding* | the run list gets step 4; a section *Seed contributors* with the interface, registration, order, environments, tenants, idempotency guidance, side effects, errors; `AfterSeed` described as the older single hook |
| `website/docs/security/authentication.md` *Setup wizard or seed* | one paragraph: what runs after the identity seed, and that wizard setups get tenant seeds on the next migrations run |
| `website/docs/features/multi-tenancy.md` | per-tenant seeds; new tenants (open question 2) |
| `website/docs/hosting/deployment.md` | contributors run on every job run; Azure job must be started after each deployment (already said) for seeds to apply |
| `website/docs/getting-started/cli.md` | `coworkee seed add`, the new `db seed` description, MCP `add_seed` |
| `website/docs/testing.md` | `RunSeedContributorAsync` and the idempotency test |
| `website/docs/reference/changelog.md` | entry |
| template `AGENTS.md`, `README.md` of `Coworkee.Identity` | the pattern line |

## 4. Migration path and backwards compatibility

- No public signature changes. `CoworkeeMigrationsOptions.DevelopmentSeed`, `DeploymentSeed`, `DevelopmentUsers`, `Attempts`, `AfterSeed`, `AddCoworkeeIdentitySeed`, `SeedCoworkeeIdentityAsync`, `SyncDevelopmentUsersAsync`, `ISetupStep`, `IdentitySeedOptions`, `SeedUser`, `SeedRole` stay, with the same order and conditions. `DemoSeed.cs` and `DemoUsers.cs` of existing apps keep working untouched.
- No schema change in the first version, so `coworkee update` needs no manual migration step.
- New behaviour only exists when an app registers a contributor. One subtle addition: `[RegisterAs]` types in the migrations project assembly are now registered; a migrations project that carries such attributes on classes for other reasons gets them in its container (harmless, they are not resolved).
- Optional follow-ups for the template (separate change, each one checked for compat): `DemoData` becomes a contributor (`EachTenant`, `Development`, `if (!context.IsNewDatabase) return;`), which also stops sample data from landing in deployments with deploy parameters; CoShipee moves its hub roles reconcile into a contributor once open question 3 is decided.

Phases:
1. `ISeedContributor`, runner, built-ins, `AddSeedContributor`, entry assembly scan, `RunSeedContributorAsync`, docs, changelog. Release.
2. `coworkee seed add` and MCP `add_seed`; template `AGENTS.md` line.
3. If wanted: `Version` with `cw.SeedHistory`; tenant seeding at run time.

## 5. Alternatives considered

- **Document `AfterSeed` and stop.** It exists and gets an `IServiceProvider`. One delegate per app, no scope, actor, transaction, order, environment or tenant loop, and modules cannot add to it. Kept for compatibility, not extended.
- **Widen `ISetupStep`.** It is shaped for the first setup (`CompleteSetupRequest`), runs inside the setup and in the API for wizard setups. Making it run every time would change what existing steps do (`LanguageSetupStep` guards itself, `ThemeSetupStep` would reconcile on every run, `DemoData` would duplicate the catalog). Kept as "inside the first setup".
- **Hosted services at service start** (as `AuthClientSeeder`). Runs in every replica at the same time, slows starts, failures do not stop a deployment. Right for configuration-owned data of one service (OIDC clients), wrong for app data.
- **EF Core `UseSeeding` / `UseAsyncSeeding`.** Runs inside `MigrateAsync` with the DbContext only: no tenant, no actor, no dispatcher, no environment; `HasData` stays fine for static lookup rows that never change.
- **A history table from the start, or a hash of the seed.** Section 3.6: schema change for every app, deleted rows stay deleted (sometimes wanted), hashes re-run untouched seeds.
- **Tags instead of the environment flags** (`[SeedTags("demo")]` plus `Seed:Tags` configuration). More flexible, but the only distinction anyone needs today is development vs. deployment, and that is already the one the demo users use.
- **One unit of work for the whole run** (ABP's original). A failing late seed would roll back the early ones, or seeds that save themselves leave partial state anyway; a transaction per call is predictable.

## 6. Risks

- Per-tenant loops on every run cost time with many tenants (CoShipee has one). Mitigation: cheap checks; a `Version` (phase 3) for the expensive ones.
- Seeded rows a user deleted come back. Mitigation: guidance (`IsNewDatabase` for user-owned samples), and `Version` later.
- Writes to other databases or blob storage are not rolled back with the app database. Mitigation: documented; those writes must be idempotent.
- The `SystemActor` passes permission checks, but tenant features or editions might still deny a request whose module is switched off for the tenant, and resource permissions (`IResourceRequest`) are untested for the system. To verify while implementing.
- Running services keep caches (permissions, settings) that a seed changes; they may serve the old values until expiry, as after `SyncDevelopmentUsersAsync` today. To verify (open question 5).
- A long or failing seed blocks every deployment. That is intended, but it makes the quality of seeds matter; the test helper and the idempotency test are the answer.

## 7. Open questions for the owner (most important first)

1. **Every run or once?** Recommended: every run, idempotent, no history table in the first version (no migration forced on apps, changed seeds reach deployed databases, deleted seeded rows come back). Add the opt-in `Version` with `cw.SeedHistory` now, later, or never?
2. **New tenants and wizard setups:** should `CreateTenant` and the setup wizard run the `EachTenant` contributors right away in the API (then they must live in modules, since the migrations project is not loaded there, and run as a background job after commit), or is "on the next migrations run, or *Re-run migrations*" enough? ABP runs them on tenant creation.
3. **Deployed databases and the identity seed:** today `DeploymentSeed` runs only on an empty database and roles/grants added later never reach a deployment (CoShipee's hub roles). Should the library ship a built-in contributor that reconciles `SeedRole`s and their grants in deployments too (add missing roles and grants, never remove), i.e. `SyncAsync` without the user and password part, outside Development? It changes deployment behaviour, so it would be opt-in (`options.SyncRolesInDeployments = true`)?
4. **Fail fast:** a failing contributor stops the run with exit code 1, so the services do not start. Or should contributors be able to declare themselves optional (`bool Required => true`), logged as warning and skipped?
5. **Caches of running services:** after a re-run against a running app, should the runner invalidate the permission and settings caches (tags `coworkee:permissions`, `coworkee:settings`) through Redis, in line with "realtime for everything", or is that out of scope for seeds?
6. **Default target:** `EachTenant` as default (recommended, `IMultiTenant` rows get their tenant without thought), or `Host` as in ABP's host-first call?
7. **Template follow-ups:** move the template's `DemoData` from `ISetupStep` to a contributor (stops sample data in deployments with deploy parameters), and mark `AfterSeed` as "prefer contributors" in its doc comment, or leave both alone?
8. **CLI name:** `coworkee seed add <Name>` (top level, like `module add`) as proposed, or under `db`?
