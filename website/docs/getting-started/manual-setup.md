# Manual setup

The [CLI](cli.md) and [Coworkee Studio](studio.md) create a Coworkee app in one step. This page builds the same kind of app by hand, with nothing but the `dotnet` command line (or the dialogs of your IDE, see [In Rider or Visual Studio](#in-rider-or-visual-studio)) and the NuGet packages. You end up with the projects of a generated app, one feature module with a table, the first migration, and an app host that starts everything.

Every command and every file on this page was run and built as shown, with the packages 0.0.1.64 and the .NET SDK 10.0.300.

## Prerequisites

- .NET SDK 10
- Docker, for Postgres, Redis and Mailpit
- `dotnet-ef` for the migration: `dotnet tool install -g dotnet-ef`

## 1. Solution and projects

The app is called `Shop`; replace the name everywhere if yours is different. The project templates come with the .NET SDK.

```bash
mkdir Shop
cd Shop
dotnet new sln -n Shop
dotnet new classlib -n Shop.Contracts -o src/Shop.Contracts
dotnet new classlib -n Shop.Application -o src/Shop.Application
dotnet new classlib -n Shop.Sales -o src/Shop.Sales
dotnet new classlib -n Shop.Infrastructure -o src/Shop.Infrastructure
dotnet new classlib -n Shop.ServiceDefaults -o src/Shop.ServiceDefaults
dotnet new worker -n Shop.Migrations -o src/Shop.Migrations
dotnet new web -n Shop.Api -o src/Shop.Api
dotnet new web -n Shop.Auth -o src/Shop.Auth
dotnet new blazor -n Shop.Web -o src --interactivity WebAssembly --all-interactive --empty
dotnet new console -n Shop.AppHost -o src/Shop.AppHost
```

The `blazor` template creates two projects at once: `src/Shop.Web` (the server) and `src/Shop.Web.Client` (the WebAssembly client).

| Project | Role |
|---|---|
| `Shop.Contracts` | DTOs, requests and permission names shared by server and client |
| `Shop.Application` | Requests and interfaces that belong to no module |
| `Shop.Sales` | A feature module: entities, tables, use cases, endpoints |
| `Shop.Infrastructure` | The database context, the migrations and the module that ties the database together |
| `Shop.ServiceDefaults` | Telemetry, health checks, service discovery and resilience for every service |
| `Shop.Migrations` | Applies the migrations and seeds, then exits |
| `Shop.Api` | The HTTP API |
| `Shop.Auth` | The sign-in server (OpenID Connect) |
| `Shop.Web`, `Shop.Web.Client` | The web app (backend for frontend) and its Blazor WebAssembly UI |
| `Shop.AppHost` | The Aspire app host: starts the services with Postgres, Redis and Mailpit |

[Solution structure](solution-structure.md) explains the projects of a generated app in more detail.

Delete what the templates bring and Coworkee replaces: the `Class1.cs` files, the worker class, the second solution file of the `blazor` template, its layout, its error and not found pages and its stylesheet.

=== "bash"

    ```bash
    rm src/Shop.Contracts/Class1.cs src/Shop.Application/Class1.cs src/Shop.Sales/Class1.cs src/Shop.Infrastructure/Class1.cs src/Shop.ServiceDefaults/Class1.cs src/Shop.Migrations/Worker.cs src/Shop.Web.sln src/Shop.Web.Client/Pages/NotFound.razor src/Shop.Web/Components/Pages/Error.razor src/Shop.Web/wwwroot/app.css
    rm -r src/Shop.Web.Client/Layout
    ```

=== "PowerShell"

    ```powershell
    Remove-Item src/Shop.Contracts/Class1.cs, src/Shop.Application/Class1.cs, src/Shop.Sales/Class1.cs, src/Shop.Infrastructure/Class1.cs, src/Shop.ServiceDefaults/Class1.cs, src/Shop.Migrations/Worker.cs, src/Shop.Web.sln, src/Shop.Web.Client/Pages/NotFound.razor, src/Shop.Web/Components/Pages/Error.razor, src/Shop.Web/wwwroot/app.css
    Remove-Item -Recurse src/Shop.Web.Client/Layout
    ```

## 2. Solution and project references

```bash
dotnet sln add src/Shop.Contracts src/Shop.Application src/Shop.Sales src/Shop.Infrastructure src/Shop.ServiceDefaults src/Shop.Migrations src/Shop.Api src/Shop.Auth src/Shop.Web src/Shop.Web.Client src/Shop.AppHost
dotnet add src/Shop.Sales reference src/Shop.Contracts src/Shop.Application
dotnet add src/Shop.Infrastructure reference src/Shop.Application src/Shop.Contracts src/Shop.Sales
dotnet add src/Shop.Migrations reference src/Shop.Infrastructure src/Shop.ServiceDefaults
dotnet add src/Shop.Api reference src/Shop.Infrastructure src/Shop.ServiceDefaults
dotnet add src/Shop.Auth reference src/Shop.Infrastructure src/Shop.ServiceDefaults
dotnet add src/Shop.Web reference src/Shop.ServiceDefaults
dotnet add src/Shop.Web.Client reference src/Shop.Contracts
dotnet add src/Shop.AppHost reference src/Shop.Migrations src/Shop.Api src/Shop.Auth src/Shop.Web
```

`dotnet sln add` reports that `Shop.Web.Client` is in the solution already: adding `Shop.Web` added the project it references.

## 3. Packages

All Coworkee packages have the same version; take the latest from the [changelog](../reference/changelog.md).

```bash
dotnet add src/Shop.Contracts package Coworkee.Contracts --version 0.0.1.64
dotnet add src/Shop.Application package Coworkee.Application --version 0.0.1.64
dotnet add src/Shop.Sales package Coworkee.Infrastructure --version 0.0.1.64
dotnet add src/Shop.Infrastructure package Coworkee.Infrastructure --version 0.0.1.64
dotnet add src/Shop.Infrastructure package Coworkee.Identity --version 0.0.1.64
dotnet add src/Shop.Infrastructure package Coworkee.AuthServer --version 0.0.1.64
dotnet add src/Shop.Infrastructure package Coworkee.Mailing --version 0.0.1.64
dotnet add src/Shop.Infrastructure package Coworkee.Theming --version 0.0.1.64
dotnet add src/Shop.Infrastructure package Coworkee.Realtime --version 0.0.1.64
dotnet add src/Shop.Infrastructure package Coworkee.Account --version 0.0.1.64
dotnet add src/Shop.Infrastructure package Coworkee.Localization --version 0.0.1.64
dotnet add src/Shop.Infrastructure package Microsoft.EntityFrameworkCore.Design --version 10.0.15
dotnet add src/Shop.Api package Coworkee.AspNetCore --version 0.0.1.64
dotnet add src/Shop.Auth package Coworkee.AuthServer --version 0.0.1.64
dotnet add src/Shop.Web package Coworkee.Bff --version 0.0.1.64
dotnet add src/Shop.Web.Client package Coworkee.Client.Blazor --version 0.0.1.64
dotnet add src/Shop.AppHost package Coworkee.Aspire --version 0.0.1.64
dotnet add src/Shop.ServiceDefaults package Microsoft.Extensions.Http.Resilience --version 10.10.0
dotnet add src/Shop.ServiceDefaults package Microsoft.Extensions.ServiceDiscovery --version 10.10.0
dotnet add src/Shop.ServiceDefaults package OpenTelemetry.Exporter.OpenTelemetryProtocol
dotnet add src/Shop.ServiceDefaults package OpenTelemetry.Extensions.Hosting
dotnet add src/Shop.ServiceDefaults package OpenTelemetry.Instrumentation.AspNetCore
dotnet add src/Shop.ServiceDefaults package OpenTelemetry.Instrumentation.Http
dotnet add src/Shop.ServiceDefaults package OpenTelemetry.Instrumentation.Runtime
dotnet add src/Shop.Web package Microsoft.AspNetCore.Components.WebAssembly.Server --version 10.0.15
dotnet add src/Shop.Web.Client package Microsoft.AspNetCore.Components.WebAssembly --version 10.0.15
```

- The Coworkee packages bring what they build on: `Coworkee.Infrastructure` brings EF Core and the Postgres, SQL Server and SQLite providers, `Coworkee.AspNetCore` the API pieces, and so on. [Packages and namespaces](../reference/packages.md) lists what each contains.
- `Shop.Infrastructure` references the modules that own tables (identity, sign-in store, mailing, themes, account, texts), so the migrations of the app create their tables. `Microsoft.EntityFrameworkCore.Design` is for `dotnet ef`.
- The last two lines raise the WebAssembly packages of the `blazor` template (10.0.8, which has a known vulnerability) to the ASP.NET Core version the Coworkee packages are built with.

!!! note "One version for all packages"
    The generated apps keep the versions in `Directory.Packages.props` ([central package management](https://learn.microsoft.com/nuget/consume-packages/central-package-management)). The project templates of the SDK write versions into the project files, which central package management rejects (`NU1008`), so this page keeps the versions in the projects. Switch later if you like: move every `Version` into a `<PackageVersion>` of `Directory.Packages.props`.

## 4. Project files and launch profile

`Shop.ServiceDefaults` is a class library, but it configures web hosts. Add the ASP.NET Core framework to the `ItemGroup` of its packages in `src/Shop.ServiceDefaults/Shop.ServiceDefaults.csproj`:

```xml title="Shop.ServiceDefaults.csproj"
<FrameworkReference Include="Microsoft.AspNetCore.App" />
```

`Shop.AppHost` was created as a console app. Give it the Aspire SDK (the version of the Aspire packages that `Coworkee.Aspire` uses) and a user secrets id, where Aspire keeps the generated database password. In `src/Shop.AppHost/Shop.AppHost.csproj` replace the first line and add the two properties after `<Nullable>`:

```xml title="Shop.AppHost.csproj"
<Project Sdk="Aspire.AppHost.Sdk/13.6.5">
  ...
    <UserSecretsId>shop-apphost</UserSecretsId>
    <NoWarn>$(NoWarn);ASPIRE010</NoWarn>
```

`ASPIRE010` only says the app host runs without the Aspire CLI bundle, which this app does not need.

The app host needs a launch profile with the addresses of the Aspire dashboard. Pick ports nothing else on your machine uses; two app hosts with the same ports do not start side by side.

```json title="src/Shop.AppHost/Properties/launchSettings.json"
{
  "profiles": {
    "https": {
      "commandName": "Project",
      "launchBrowser": true,
      "applicationUrl": "https://localhost:17280;http://localhost:15280",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development",
        "DOTNET_ENVIRONMENT": "Development",
        "ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL": "https://localhost:21280",
        "ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL": "https://localhost:22280"
      }
    }
  }
}
```

## 5. The code

### Application

```csharp title="src/Shop.Application/ShopApplicationModule.cs"
using Coworkee.Application;
using Coworkee.Application.Messaging;
using Coworkee.Core.Modularity;

namespace Shop.Application;

[DependsOn(typeof(CoworkeeApplicationModule))]
public sealed class ShopApplicationModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context) =>
        context.Services.AddMessagingFromAssembly(typeof(ShopApplicationModule).Assembly);
}
```

### The feature module

A module class, one entity and its table. [Your first feature](first-feature.md) adds the permissions, a command, an endpoint, the OData set and a page.

```csharp title="src/Shop.Sales/SalesModule.cs"
using Coworkee.Application.Messaging;
using Coworkee.Core.Modularity;

namespace Shop.Sales;

public sealed class SalesModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context) =>
        context.Services.AddMessagingFromAssembly(typeof(SalesModule).Assembly);
}
```

```csharp title="src/Shop.Sales/Domain/Order.cs"
using Coworkee.Domain;

namespace Shop.Sales.Domain;

public sealed class Order : AuditedEntity, IMultiTenant
{
    public string Number { get; set; } = string.Empty;

    public Guid TenantId { get; set; }
}
```

```csharp title="src/Shop.Sales/Persistence/SalesModelContributor.cs"
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nextended.Core.Attributes;
using Shop.Sales.Domain;

namespace Shop.Sales.Persistence;

[RegisterAs(typeof(IModelContributor), ServiceLifetime = ServiceLifetime.Singleton)]
internal sealed class SalesModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder) => modelBuilder.Entity<Order>(entity =>
    {
        entity.ToTable("Orders", "app");
        entity.Property(e => e.Number).HasMaxLength(50);
    });
}
```

`[RegisterAs]` registers the class when the module is loaded, see [Modules](../fundamentals/modules.md).

### Infrastructure

```csharp title="src/Shop.Infrastructure/ShopDbContext.cs"
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Shop.Infrastructure;

public sealed class ShopDbContext(DbContextOptions<ShopDbContext> options, ICurrentUser currentUser, IEnumerable<IModelContributor> contributors)
    : CoworkeeDbContext(options, currentUser, contributors);
```

```csharp title="src/Shop.Infrastructure/ShopInfrastructureModule.cs"
using Coworkee.Contracts.Configuration;
using Coworkee.Core.Modularity;
using Coworkee.Identity;
using Coworkee.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application;

namespace Shop.Infrastructure;

[DependsOn(typeof(ShopApplicationModule), typeof(CoworkeeIdentityModule))]
public sealed class ShopInfrastructureModule : CoworkeeModule
{
    public const string ConnectionStringName = "shop";

    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddCoworkeeDbContext<ShopDbContext>((provider, options) =>
            options.UseCoworkeeDatabase(provider.GetRequiredService<IConfiguration>(), ConnectionStringName));
        context.Services.PostConfigure<BackgroundJobOptions>(options => options.ConnectionStringName = ConnectionStringName);
    }
}
```

The connection string name is the name of the app in the app host (`AddCoworkeeApp("shop")`); the background jobs use the same database.

```csharp title="src/Shop.Infrastructure/ShopDatabaseModule.cs"
using Coworkee.Account;
using Coworkee.AuthServer;
using Coworkee.Core.Modularity;
using Coworkee.Localization;
using Coworkee.Mailing;
using Coworkee.Theming;
using Shop.Sales;

namespace Shop.Infrastructure;

// every module that owns tables, so the migrations cover all of them
[DependsOn(typeof(SalesModule), typeof(ShopInfrastructureModule), typeof(CoworkeeAuthStoreModule), typeof(CoworkeeMailingModule),
    typeof(CoworkeeThemingModule), typeof(CoworkeeAccountModule), typeof(CoworkeeLocalizationModule))]
public sealed class ShopDatabaseModule : CoworkeeModule;
```

Each new module of the app goes into this list.

```csharp title="src/Shop.Infrastructure/ShopDbContextDesignFactory.cs"
using Coworkee.Contracts.Configuration;
using Coworkee.Core.Modularity;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Shop.Infrastructure;

// lets dotnet ef build the context; the connection is never opened, migrations are made from the model
public sealed class ShopDbContextDesignFactory : IDesignTimeDbContextFactory<ShopDbContext>
{
    public ShopDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new($"ConnectionStrings:{ShopInfrastructureModule.ConnectionStringName}", "Host=localhost;Database=shop_design"),
                new(DatabaseOptions.ProviderKey, DatabaseProviders.Postgres)])
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCoworkeeModules<ShopDatabaseModule>(configuration);
        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<ShopDbContext>();
    }
}
```

### Service defaults

```csharp title="src/Shop.ServiceDefaults/Extensions.cs"
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

public static class Extensions
{
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });
        var telemetry = builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation())
            .WithTracing(tracing => tracing.AddSource(builder.Environment.ApplicationName).AddAspNetCoreInstrumentation().AddHttpClientInstrumentation());
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            telemetry.UseOtlpExporter(); // the Aspire dashboard
        }

        builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });
        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/alive", new() { Predicate = check => check.Tags.Contains("live") });
        return app;
    }
}
```

This is a short form of the service defaults of an Aspire project. The app host watches `/health` of the API, the sign-in server and the web app; `MapDefaultEndpoints` maps it.

### Migrations

```csharp title="src/Shop.Migrations/Program.cs"
using Coworkee.Core.Modularity;
using Coworkee.Identity.Setup;
using Shop.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddCoworkeeModules<ShopDatabaseModule>(builder.Configuration);
builder.AddCoworkeeMigrations<ShopDbContext>(options =>
{
    // a local run: the organisation and its first administrator
    options.DevelopmentSeed = seed =>
    {
        seed.TenantName = "Shop";
        seed.Users.Add(new SeedUser("admin@shop.local", "Admin123!", "Admin", null, IsAdmin: true));
    };
    // a deployment: the administrator comes from the deploy parameters
    options.DeploymentSeed = (seed, admin) =>
    {
        seed.TenantName = "Shop";
        seed.Users.Add(admin);
    };
});
using var host = builder.Build();
return await host.RunCoworkeeMigrationsAsync();
```

The host applies the migrations with retries, seeds and exits; the other services start after it. See [migrations and seeding](../fundamentals/persistence.md#migrations-and-seeding).

### API and sign-in server

```csharp title="src/Shop.Api/Program.cs"
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Authentication;
using Coworkee.Core.Modularity;
using Coworkee.Infrastructure.Outbox;
using Coworkee.Settings;
using Shop.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Configuration.AddCoworkeeDatabaseConfiguration(ShopInfrastructureModule.ConnectionStringName);
builder.AddCoworkee<ShopApiModule>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.UseCoworkee();
app.Run();

[DependsOn(typeof(ShopDatabaseModule))]
internal sealed class ShopApiModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddCoworkeeOutboxProcessing<ShopDbContext>(); // sends the events the commands stored
        context.Services.AddCoworkeeApiAuthentication(context.Configuration, "shop_api"); // "<app name>_api", see the app host
    }
}
```

The audience `shop_api` has to be the app name of the app host followed by `_api`; with another value every call of the web app is rejected with 401.

```csharp title="src/Shop.Auth/Program.cs"
using Coworkee.AspNetCore;
using Coworkee.AuthServer;
using Coworkee.Core.Modularity;
using Coworkee.Settings;
using Shop.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Configuration.AddCoworkeeDatabaseConfiguration(ShopInfrastructureModule.ConnectionStringName);
builder.AddCoworkee<ShopAuthModule>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.UseCoworkee();
app.Run();

[DependsOn(typeof(ShopInfrastructureModule), typeof(CoworkeeAuthServerModule))]
internal sealed class ShopAuthModule : CoworkeeModule;
```

`AddCoworkeeDatabaseConfiguration` lays the settings changed in the app over the configuration, see [typed app settings](../modules/settings.md#typed-app-settings).

### Web app

```csharp title="src/Shop.Web/Program.cs"
using Coworkee.Bff;
using Shop.Web.Components;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddCoworkeeBff();
builder.Services.AddRazorComponents().AddInteractiveWebAssemblyComponents();

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}

app.UseCoworkeeSecurityHeaders();
app.MapDefaultEndpoints();
app.UseStaticFiles();
app.UseAntiforgery();
app.MapCoworkeeBff();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Shop.Web.Client._Imports).Assembly, typeof(Coworkee.Client.Blazor.CoworkeeClientOptions).Assembly);
app.Run();
```

```razor title="src/Shop.Web/Components/App.razor"
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <base href="/" />
    <title>Shop</title>
    <link href="_content/MudBlazor/MudBlazor.min.css" rel="stylesheet" />
    <link href="_content/MudBlazor.Extensions/mudBlazorExtensions.min.css" rel="stylesheet" />
    <link href="@Assets["Shop.Web.styles.css"]" rel="stylesheet" />
    <HeadOutlet @rendermode="new InteractiveWebAssemblyRenderMode(prerender: false)" />
</head>
<body>
    <Routes @rendermode="new InteractiveWebAssemblyRenderMode(prerender: false)" />
    <script src="_content/MudBlazor/MudBlazor.min.js"></script>
    <script src="_framework/blazor.web.js"></script>
</body>
</html>
```

The client renders only in the browser (`prerender: false`), as in a generated app; the stylesheets and the script of MudBlazor come from the packages.

### Blazor client

```csharp title="src/Shop.Web.Client/Program.cs"
using Coworkee.Client.Blazor;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Services.AddCoworkeeClient(new Uri(builder.HostEnvironment.BaseAddress), options => options.AppTitle = "Shop");
var host = builder.Build();
await host.Services.InitializeCoworkeeClientAsync();
await host.RunAsync();
```

```razor title="src/Shop.Web.Client/Routes.razor"
<Router AppAssembly="typeof(Program).Assembly" AdditionalAssemblies="new[] { typeof(CoworkeeClientOptions).Assembly }">
    <Found Context="routeData">
        <AuthorizeRouteView RouteData="routeData" DefaultLayout="typeof(CoworkeeLayout)">
            <NotAuthorized>
                <NotAllowed PageType="routeData.PageType" />
            </NotAuthorized>
        </AuthorizeRouteView>
        <FocusOnNavigate RouteData="routeData" Selector="h1" />
    </Found>
    <NotFound>
        <LayoutView Layout="typeof(CoworkeeLayout)"><MudText>Not found.</MudText></LayoutView>
    </NotFound>
</Router>
```

The router adds the pages of `Coworkee.Client.Blazor` (account, administration) next to the app's own, and `CoworkeeLayout` is the shell with menu, search and user menu.

```razor title="src/Shop.Web.Client/_Imports.razor"
@using System.Net.Http
@using System.Net.Http.Json
@using Microsoft.AspNetCore.Authorization
@using Microsoft.AspNetCore.Components.Authorization
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using static Microsoft.AspNetCore.Components.Web.RenderMode
@using Microsoft.JSInterop
@using MudBlazor
@using Coworkee.Client.Blazor
@using Coworkee.Client.Blazor.Components
@using Shop.Web.Client
```

```razor title="src/Shop.Web.Client/Pages/Home.razor"
@page "/"

<PageTitle>Shop</PageTitle>

<MudText Typo="Typo.h3">Hello, Shop!</MudText>
```

### App host

Replace the content of `src/Shop.AppHost/Program.cs`:

```csharp title="src/Shop.AppHost/Program.cs"
var builder = DistributedApplication.CreateBuilder(args);
var app = builder.AddCoworkeeApp("shop");

app.AddMigrations<Projects.Shop_Migrations>();
app.AddAuthServer<Projects.Shop_Auth>();
app.AddApi<Projects.Shop_Api>();
app.AddWeb<Projects.Shop_Web>();

builder.Build().Run();
```

`AddCoworkeeApp` adds the containers the packages need (Postgres, Redis, Mailpit) and gives every service the connection strings, addresses and sign-in clients it needs, see [Aspire app host](../hosting/aspire.md). That is why none of the hosts needs an entry in its `appsettings.json`; the files of the templates stay as they are.

## 6. The first migration

```bash
dotnet ef migrations add Initial --project src/Shop.Infrastructure --startup-project src/Shop.Infrastructure
```

The migration lands in `src/Shop.Infrastructure/Migrations` and creates the tables of all modules in `ShopDatabaseModule`, `app.Orders` among them. After every later model change add the next one the same way, see [Persistence](../fundamentals/persistence.md).

## 7. Build and run

```bash
dotnet build Shop.slnx
dotnet run --project src/Shop.AppHost
```

The build ends without errors. It shows warnings `WASM0001` about SQLite functions in the WebAssembly client; a generated app shows them as well, they do no harm.

With Docker running, the console prints the address of the Aspire dashboard (`https://localhost:17280` with the launch profile above). The migration service runs first and finishes; then `shop-auth`, `shop-api` and `shop-web` start. Open the address of `shop-web` from the dashboard, choose **Sign in** and use `admin@shop.local` with the password `Admin123!`. The shell of the app shows the home page, and **Administration** has the users, roles, settings and the other pages of the packages.

## In Rider or Visual Studio

The same steps work in the dialogs of the IDE. The menus have slightly different names from version to version; what matters is the project type and the settings.

**New solution.** Rider: **File | New Solution**, choose **Empty Solution**, name it `Shop`. Visual Studio: **File > New > Project**, template **Blank Solution**, name it `Shop`.

**Projects.** Right-click the solution, **Add > New Project** (Rider) or **Add > New Project…** (Visual Studio), and create, each in a folder `src/<name>`:

- **Class Library** for `Shop.Contracts`, `Shop.Application`, `Shop.Sales`, `Shop.Infrastructure` and `Shop.ServiceDefaults`
- **Worker Service** for `Shop.Migrations`
- **ASP.NET Core Empty** (Rider: **ASP.NET Core Web Application** with the template **Empty**) for `Shop.Api` and `Shop.Auth`
- **Blazor Web App** for `Shop.Web` with render mode **WebAssembly**, interactivity **Global** and without sample pages; the IDE creates `Shop.Web.Client` with it
- **Console App** for `Shop.AppHost`

Then delete the files of step 1 in the solution explorer.

**References.** Right-click a project, **Add > Reference…** (Rider) or **Add > Project Reference…** (Visual Studio), and tick the projects of step 2.

**Packages.** Right-click a project, **Manage NuGet Packages** (in Rider also **Tools | NuGet | Manage NuGet Packages for Solution**), search for `Coworkee.`, pick the version `0.0.1.64` and install the packages of step 3 into their projects. In the solution-wide window you can tick several projects for one package.

**Project files.** Rider: right-click the project, **Edit > Edit 'Shop.AppHost.csproj'**. Visual Studio: double-click the project. Make the changes of step 4 and add `Properties/launchSettings.json` to the app host.

**Code.** Add the files of step 5 with **Add > Class** or **Add > File**, replacing the content.

**Migration.** Run the command of step 6 in the terminal of the IDE (Rider **View | Tool Windows | Terminal**, Visual Studio **View > Terminal**) from the solution folder.

**Run.** Make `Shop.AppHost` the startup project (Visual Studio: right-click, **Set as Startup Project**; Rider: choose `Shop.AppHost: https` in the run configurations) and start it. The browser opens the Aspire dashboard.

## What the CLI adds

A generated app has more than this minimum, all of which you can add by hand later:

- `Directory.Build.props` (warnings as errors, code style) and `Directory.Packages.props` with one version for all packages, a `global.json`
- the texts of the app as `Localization/<culture>.json` in the contracts, a theme class, navigation, dialog options
- demo users and roles with their permissions (`DemoUsers.cs`, `DemoSeed.cs`, `LOGIN.md`), the sample modules
- feature modules picked as options (notifications, chat, AI assistant, reports, backup, …), each a package plus a line in the database module
- test projects, `AGENTS.md` for coding agents, a CI workflow

[Command line](cli.md) lists the options, [Solution structure](solution-structure.md) shows what a generated app contains.

## Next

[Your first feature](first-feature.md) builds an entity with permissions, a command, an endpoint, an OData set and a Blazor page.
