# Solution structure

The template separates what runs where. Every service is a small host that composes modules; the feature code lives in module projects.

```mermaid
flowchart LR
    Browser -->|cookie| Web[MyApp.Web<br/>BFF + Blazor WASM]
    Web -->|bearer token| Api[MyApp.Api]
    Web -->|OpenID Connect| Auth[MyApp.Auth]
    Auth -->|external login| Keycloak
    Api --> Db[(Postgres)]
    Auth --> Db
    Migrations[MyApp.Migrations] --> Db
```

| Project | Role |
|---|---|
| `MyApp.AppHost` | Aspire app host, see [Aspire app host](../hosting/aspire.md) |
| `MyApp.Migrations` | Applies EF Core migrations, seeds users, roles and sample data, then exits |
| `MyApp.Auth` | OpenID Connect server with the sign-in pages (`Coworkee.AuthServer`) |
| `MyApp.Api` | The HTTP API: module endpoints, OData, SignalR hub, Hangfire dashboard |
| `MyApp.Web` | Hosts the WebAssembly client and acts as backend for frontend (`Coworkee.Bff`) |
| `MyApp.Web.Client` | Blazor WebAssembly: pages, dialogs, navigation |
| `MyApp.Contracts` | DTOs, requests and permission names shared by server and client |
| `MyApp.Catalog`, `MyApp.Documents` | Feature modules: domain, persistence, features, endpoints |
| `MyApp.Application` | Interfaces modules share, for example the dashboard counts |
| `MyApp.Infrastructure` | `MyAppDbContext`, migrations, the design time factory for `dotnet ef` and the module that ties the database together (`MyAppDatabaseModule`) |
| `MyApp.ServiceDefaults` | OpenTelemetry, health checks, service discovery and resilience for every service |
| `MyApp.AirWorks`, `MyApp.ElsaStudio` | Only with `--airworks` (the [AirWorks client](../web/airworks.md)) or `--elsa-studio` (the [workflow designer](../modules/workflows-elsa.md#elsa-studio)) |
| `tests/` | `MyApp.Api.Tests` against a real database, `MyApp.Migrations.Tests`, `MyApp.Web.Client.Tests` (bUnit) and `MyApp.AppHost.Tests`, see [Testing](../testing.md) |

At the root: `README.md`, `LOGIN.md` (the development users), `quickrun.yml`, and `AGENTS.md` with the rules for coding agents (`CLAUDE.md` imports it), see [Building with AI](../features/building-with-ai.md).

## Inside a feature module

```text
MyApp.Catalog/
  CatalogModule.cs
  Domain/                Brand.cs, Product.cs
  Persistence/           CatalogModelContributor.cs
  Permissions/           CatalogPermissionDefinitions.cs
  Features/
    Brands/
      Commands/AddEdit/  AddEditBrandCommand.cs, ...Handler.cs, ...Validator.cs
      Commands/Delete/
      Queries/GetById/
      BrandErrors.cs, BrandMapping.cs
  Endpoints/             BrandEndpoints.cs
```

One type per file, the use case in the folder name. Lists do not need a query: the OData set of the entity serves the tables.

## How the hosts start

Every server host has the same few lines; the root module decides what the host contains.

```csharp title="MyApp.Api/Program.cs"
var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Configuration.AddCoworkeeDatabaseConfiguration(MyAppInfrastructureModule.ConnectionStringName); // values changed in the app
builder.AddCoworkee<MyAppApiModule>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.UseCoworkee();
app.Run();
```

```csharp title="MyApp.Auth/Program.cs"
[DependsOn(typeof(MyAppInfrastructureModule), typeof(CoworkeeAuthServerModule))]
internal sealed class MyAppAuthModule : CoworkeeModule;
```
