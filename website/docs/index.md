---
hide:
  - navigation
---

# Coworkee

Coworkee is a modular application framework for .NET 10. It gives a Blazor and ASP.NET Core application the parts every business app needs, so the app itself can stay about its domain: modules with dependencies, a request pipeline with validation, permissions and caching, EF Core with audit and outbox, an auth server with external sign-in, a Blazor shell with data tables and admin pages, and an Aspire app host that wires all of it.

![The demo application built on Coworkee](assets/screenshots/products.png){ .shot }

<div class="grid cards" markdown>

-   **Start a new app**

    Clone the template, run the app host, sign in with the seeded administrator.

    [:octicons-arrow-right-24: Installation](getting-started/installation.md)

-   **Build a feature**

    Entity, permissions, command, endpoint, OData set and a Blazor page with facets, step by step.

    [:octicons-arrow-right-24: Your first feature](getting-started/first-feature.md)

-   **Understand the pieces**

    Modules, the dispatcher and its middleware, persistence conventions.

    [:octicons-arrow-right-24: Fundamentals](fundamentals/modules.md)

-   **Change the UI**

    Replace any component of the shell, add navigation, build tables.

    [:octicons-arrow-right-24: Blazor UI](ui/client.md)

</div>

## What is in the box

| Area | You get |
|---|---|
| Modules | `CoworkeeModule` with `[DependsOn]`, one call `AddCoworkee<TRoot>()` per host |
| Requests | `IDispatcher` with commands, queries, `Result`, FluentValidation, permission checks, caching, unit of work |
| Data | `CoworkeeDbContext` with tenants, soft delete, field level audit trail, transactional outbox, realtime change events |
| HTTP | Minimal API groups with problem details, OData sets with facets for every entity, response filters |
| Identity | Users, roles, groups, tenants, permission grants down to single resources, setup wizard or seed |
| Sign-in | OpenIddict auth server, BFF for the Blazor client, Keycloak or any OpenID Connect provider as external login |
| Blazor | MudBlazor shell with navigation, admin pages, data table with facets, theming, replaceable components |
| Services | Settings, typed app configuration, mail templates, Hangfire jobs, blob storage, Elasticsearch, notifications, AI assistant with MCP |
| Hosting | `AddCoworkeeApp` for Aspire: Postgres, Redis, Mailpit, Elasticsearch and Keycloak wired from the packages each service uses |

## A taste

```csharp title="Catalog module"
[DependsOn(typeof(CoworkeeODataModule))]
public sealed class MyAppCatalogModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddMessagingFromAssembly(typeof(MyAppCatalogModule).Assembly);
        context.Services.AddSingleton<IModelContributor, CatalogModelContributor>();
        context.Services.AddSingleton<IPermissionDefinitionContributor, CatalogPermissionDefinitions>();
        context.Services.AddODataEntity<Product>("Products", CatalogPermissions.Products.View);
    }

    public void ConfigureApplication(WebApplication app) => app.MapProductEndpoints();
}
```

```csharp title="App host"
var app = builder.AddCoworkeeApp("myapp", options => options.UseKeycloak());

app.AddMigrations<Projects.MyApp_Migrations>();
app.AddAuthServer<Projects.MyApp_Auth>();
app.AddApi<Projects.MyApp_Api>();
app.AddWeb<Projects.MyApp_Web>();
```

## Where Coworkee comes from

Coworkee grew out of [CleanArchitectureBaseBlazor](https://github.com/fgilde/CleanArchitectureBaseBlazor). The template is now a thin app on top of the packages, and [Sharemee](https://github.com/fgilde/shareme), a digital asset management system, is the second app built on them. The UI relies on [MudBlazor](https://mudblazor.com) and [MudBlazor.Extensions](https://github.com/fgilde/MudBlazor.Extensions); facets, response filters and code generation come from [Nextended](https://github.com/fgilde/Nextended).

The ideas will look familiar if you know ABP: modules with dependencies, permission definitions, settings, replaceable UI. Coworkee keeps less ceremony: no repositories over EF Core, no application service layer, requests and handlers instead.
