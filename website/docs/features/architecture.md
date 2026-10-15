---
title: "Modular monolith architecture for ASP.NET Core"
description: "Coworkee builds .NET business apps as a modular monolith on ASP.NET Core: modules with [DependsOn], one request pipeline, an outbox, and a CLI that scaffolds it."
---

# A modular monolith for ASP.NET Core, with the tooling to start one

Most business apps start as one project and end up as a tangle: every new feature edits the same startup file, the same `DbContext` and the same menu. Coworkee splits an app into modules from day one, but keeps them in one deployable unit with one database. You get the boundaries of services without the network hops, the distributed transactions and the deployment overhead.

The second half of the problem is getting started. A new solution with an API, an auth server, a Blazor client, migrations and an Aspire app host is a day of wiring before the first entity exists. The `coworkee` command line, its desktop wizard Coworkee Studio and an MCP server for AI agents create that solution in minutes, with your own modules and entities already in it.

![Coworkee Studio on the Architecture page, with presets for the app type and the infrastructure options](../assets/screenshots/studio-architecture-light.webp){ .shot loading=lazy }

## Modules with `[DependsOn]`

A module is a class that registers its services and, in web hosts, maps its endpoints. It names the modules it needs with `[DependsOn]`, and Coworkee loads the whole graph once, in dependency order:

```csharp
[DependsOn(typeof(CoworkeeODataModule), typeof(CoworkeeStorageModule))]
public sealed class MyAppDocumentsModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddMessagingFromAssembly(typeof(MyAppDocumentsModule).Assembly);
        context.Services.AddODataEntity<Document>("Documents", DocumentPermissions.Documents.View, d => d.BlobKey);
    }

    public void ConfigureApplication(WebApplication app) => app.MapDocumentEndpoints();
}
```

Modules never edit a central list. Entity mappings, permissions, settings, mail templates and menu entries come in through contributors (`IModelContributor`, `IPermissionDefinitionContributor`, `ISettingDefinitionContributor`, `INavigationContributor` and a few more) that the owning package collects. Adding or removing a module touches nothing else.

## One root module per host

Every server host in the template has the same few lines. The root module decides what the host contains:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddCoworkee<MyAppApiModule>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.UseCoworkee();
app.Run();
```

The API, the auth server and the migration service compose different graphs from the same modules. The auth host only pulls in infrastructure and the auth server module; a module outside a host's graph costs that host nothing. Hosts without HTTP call `services.AddCoworkeeModules<TRoot>(configuration)` instead.

## One request pipeline for every use case

Each use case is a command or a query sent through `IDispatcher`. HTTP endpoints, background jobs, the AI assistant and tests all send the same requests, so the same rules apply everywhere. Middleware wraps each request in a fixed order:

| Order | Step | What it does |
|---|---|---|
| 100 | Logging | logs the request, warns above 500 ms |
| 200 | Authorization | checks `[RequiresPermission]` on the request type |
| 300 | Validation | runs the FluentValidation validators, returns field messages |
| 350 | Caching | answers `ICachedQuery` from HybridCache, clears tags after `IInvalidatesCache` |
| 400 | Unit of work | saves the `DbContext` once after a successful command |

```csharp
[RequiresPermission(CatalogPermissions.Brands.Delete)]
public sealed record DeleteBrandsCommand(IReadOnlyList<Guid> Ids) : ICommand<Result>, IInvalidatesCache
{
    public IReadOnlyList<string> CacheTags => [DashboardCache.Tag];
}
```

Handlers return a `Result` instead of throwing for expected failures, and `ToHttpResult` maps validation, not found, forbidden and conflict errors to 400, 404, 403 and 409. Your own cross-cutting code plugs in with `IRequestMiddleware` or a typed `IRequestBehavior`.

## Domain events, integration events and the outbox

Aggregates raise domain events; their handlers run after the transaction committed, through the outbox, as the user who caused them. Integration events go across app boundaries with `IEventBus` and use the same outbox, so an event leaves only when the change behind it is saved. Without a transport the handlers of the same app receive it directly; with `Coworkee.EventBus.RabbitMq` it travels over RabbitMQ with retries and a dead letter queue. Handlers are idempotent through an inbox table, and `IDistributedLock` keeps a job from running twice across instances.

## NuGet packages, not a copied code base

Coworkee ships as NuGet packages that all start with `Coworkee.`: `Coworkee.Core`, `Coworkee.Application`, `Coworkee.Infrastructure`, `Coworkee.AspNetCore`, `Coworkee.Client.Blazor`, `Coworkee.Aspire` and the feature modules on top. A generated app references them with one version in `Directory.Packages.props`, and `coworkee update` moves that version forward. Your code stays in your projects; the framework code stays in packages you can update.

## The `coworkee` command line

```bash
dotnet tool install -g Coworkee.Cli
coworkee new Shop --no-samples --files --theme Forest --primary-color "#0a7d5a"
cd Shop
coworkee run
```

`coworkee new` creates the solution (app host, API, auth server, Blazor web app, migrations), restores it and adds the initial migration when the chosen modules need one. A JSON spec describes the whole app, including your own modules, entities, enums, roles and development users: `coworkee spec example`, `coworkee spec validate`, `coworkee new --spec shop.json`. Later, `coworkee module add` and `coworkee entity add` generate a feature module or entity end to end: contracts, entity, table, permissions, OData set, commands with validators, endpoints and a Blazor page with a data table. `coworkee doctor` checks the SDK, Docker and `dotnet-ef`.

## Coworkee Studio, the desktop wizard

`coworkee ui` installs and opens Coworkee Studio, which runs on Windows, macOS and Linux. It walks through basics, branding, sign-in design, architecture, languages, entities and users page by page, explains what each option brings and costs, and lets the CLI create the solution. The Architecture page offers presets from *Minimal* to *SaaS platform*; the Entities page shows what the CLI will generate for each entity. After creation the Studio can start the app host and links the Aspire dashboard and the app.

![Entity designer in Coworkee Studio with a property grid and a preview of the generated routes and files](../assets/screenshots/studio-entities-light.webp){ .shot loading=lazy }

## An MCP server for AI agents

`coworkee mcp` runs a Model Context Protocol server over stdio. Claude Code, Claude Desktop or VS Code can then read the options and the spec schema, write and validate a spec, create the app and add modules, entities and migrations, with the same commands you would type:

```bash
claude mcp add coworkee -- coworkee mcp
```

This server lives on your machine. A generated app has its own MCP endpoint with the tools of the app.

## Where to go next

- [Modules](../fundamentals/modules.md): module lifecycle, root modules and contributors
- [Messaging and pipeline](../fundamentals/messaging.md): commands, queries, results, middleware and caching
- [Events and locks](../fundamentals/events.md): integration events, RabbitMQ and distributed locks
- [Solution structure](../getting-started/solution-structure.md): the projects of a generated app
- [Command line](../getting-started/cli.md): every option of `coworkee new` and the app spec format
- [Coworkee Studio](../getting-started/studio.md): the wizard page by page
- [MCP server of the CLI](../getting-started/mcp.md): registration and tools
- [Packages and namespaces](../reference/packages.md): which package goes where
