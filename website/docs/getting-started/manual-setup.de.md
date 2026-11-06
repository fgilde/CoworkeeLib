# Manuelle Einrichtung

Die [CLI](cli.md) und [Coworkee Studio](studio.md) legen eine Coworkee-App in einem Schritt an. Diese Seite baut dieselbe Art App von Hand, nur mit der `dotnet`-Kommandozeile (oder den Dialogen deiner IDE, siehe [In Rider oder Visual Studio](#in-rider-oder-visual-studio)) und den NuGet-Paketen. Am Ende hast du die Projekte einer generierten App, ein Feature-Modul mit einer Tabelle, die erste Migration und einen App-Host, der alles startet.

Jeder Befehl und jede Datei dieser Seite wurde genau so ausgeführt und gebaut, mit den Paketen 0.0.1.64 und dem .NET SDK 10.0.300.

## Voraussetzungen

- .NET SDK 10
- Docker, für Postgres, Redis und Mailpit
- `dotnet-ef` für die Migration: `dotnet tool install -g dotnet-ef`

## 1. Solution und Projekte

Die App heißt `Shop`; ersetze den Namen überall, wenn deine anders heißt. Die Projektvorlagen bringt das .NET SDK mit.

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

Die Vorlage `blazor` legt zwei Projekte auf einmal an: `src/Shop.Web` (den Server) und `src/Shop.Web.Client` (den WebAssembly-Client).

| Projekt | Aufgabe |
|---|---|
| `Shop.Contracts` | DTOs, Requests und Berechtigungsnamen, die Server und Client teilen |
| `Shop.Application` | Requests und Interfaces, die zu keinem Modul gehören |
| `Shop.Sales` | Ein Feature-Modul: Entities, Tabellen, Use Cases, Endpunkte |
| `Shop.Infrastructure` | Der Datenbankkontext, die Migrationen und das Modul, das die Datenbank zusammenführt |
| `Shop.ServiceDefaults` | Telemetrie, Health Checks, Service Discovery und Resilienz für jeden Dienst |
| `Shop.Migrations` | Wendet die Migrationen an, seedet und beendet sich |
| `Shop.Api` | Die HTTP-API |
| `Shop.Auth` | Der Anmeldeserver (OpenID Connect) |
| `Shop.Web`, `Shop.Web.Client` | Die Web-App (Backend for Frontend) und ihre Blazor-WebAssembly-Oberfläche |
| `Shop.AppHost` | Der Aspire-App-Host: startet die Dienste mit Postgres, Redis und Mailpit |

[Aufbau der Solution](solution-structure.md) erklärt die Projekte einer generierten App genauer.

Lösche, was die Vorlagen mitbringen und Coworkee ersetzt: die `Class1.cs`-Dateien, die Worker-Klasse, die zweite Solution-Datei der Vorlage `blazor`, ihr Layout, ihre Fehler- und Nicht-gefunden-Seite und ihr Stylesheet.

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

## 2. Solution- und Projektverweise

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

`dotnet sln add` meldet, dass `Shop.Web.Client` schon in der Solution ist: Mit `Shop.Web` kam das Projekt dazu, das es referenziert.

## 3. Pakete

Alle Coworkee-Pakete haben dieselbe Version; die neueste steht im [Changelog](../reference/changelog.md).

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
dotnet add src/Shop.Infrastructure package Microsoft.EntityFrameworkCore.Design --version 10.0.14
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
dotnet add src/Shop.Web package Microsoft.AspNetCore.Components.WebAssembly.Server --version 10.0.14
dotnet add src/Shop.Web.Client package Microsoft.AspNetCore.Components.WebAssembly --version 10.0.14
```

- Die Coworkee-Pakete bringen mit, worauf sie aufbauen: `Coworkee.Infrastructure` bringt EF Core und die Provider für Postgres, SQL Server und SQLite, `Coworkee.AspNetCore` die Teile der API und so weiter. [Pakete und Namespaces](../reference/packages.md) listet, was jedes enthält.
- `Shop.Infrastructure` referenziert die Module mit eigenen Tabellen (Identität, Anmeldespeicher, Mailing, Themes, Konto, Texte), damit die Migrationen der App ihre Tabellen anlegen. `Microsoft.EntityFrameworkCore.Design` braucht `dotnet ef`.
- Die letzten beiden Zeilen heben die WebAssembly-Pakete der Vorlage `blazor` (10.0.8, mit einer bekannten Sicherheitslücke) auf die ASP.NET-Core-Version, mit der die Coworkee-Pakete gebaut sind.

!!! note "Eine Version für alle Pakete"
    Die generierten Apps halten die Versionen in `Directory.Packages.props` ([zentrale Paketverwaltung](https://learn.microsoft.com/nuget/consume-packages/central-package-management)). Die Projektvorlagen des SDK schreiben Versionen in die Projektdateien, was die zentrale Paketverwaltung ablehnt (`NU1008`); deshalb bleiben die Versionen auf dieser Seite in den Projekten. Umstellen kannst du später: jede `Version` in ein `<PackageVersion>` der `Directory.Packages.props` verschieben.

## 4. Projektdateien und Startprofil

`Shop.ServiceDefaults` ist eine Klassenbibliothek, konfiguriert aber Web-Hosts. Ergänze in `src/Shop.ServiceDefaults/Shop.ServiceDefaults.csproj` das ASP.NET-Core-Framework in der `ItemGroup` der Pakete:

```xml title="Shop.ServiceDefaults.csproj"
<FrameworkReference Include="Microsoft.AspNetCore.App" />
```

`Shop.AppHost` entstand als Konsolen-App. Gib ihm das Aspire-SDK (die Version der Aspire-Pakete, die `Coworkee.Aspire` nutzt) und eine User-Secrets-Id, unter der Aspire das generierte Datenbankpasswort ablegt. Ersetze in `src/Shop.AppHost/Shop.AppHost.csproj` die erste Zeile und ergänze die beiden Eigenschaften nach `<Nullable>`:

```xml title="Shop.AppHost.csproj"
<Project Sdk="Aspire.AppHost.Sdk/13.6.4">
  ...
    <UserSecretsId>shop-apphost</UserSecretsId>
    <NoWarn>$(NoWarn);ASPIRE010</NoWarn>
```

`ASPIRE010` meldet nur, dass der App-Host ohne das Aspire-CLI-Bundle läuft, das diese App nicht braucht.

Der App-Host braucht ein Startprofil mit den Adressen des Aspire-Dashboards. Nimm Ports, die auf deinem Rechner sonst niemand nutzt; zwei App-Hosts mit denselben Ports starten nicht nebeneinander.

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

## 5. Der Code

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

### Das Feature-Modul

Eine Modulklasse, eine Entity und ihre Tabelle. [Das erste Feature](first-feature.md) ergänzt Berechtigungen, einen Command, einen Endpunkt, das OData-Set und eine Seite.

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

`[RegisterAs]` registriert die Klasse, wenn das Modul geladen wird, siehe [Module](../fundamentals/modules.md).

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

Der Name des Connection Strings ist der Name der App im App-Host (`AddCoworkeeApp("shop")`); die Hintergrundjobs nutzen dieselbe Datenbank.

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

Jedes neue Modul der App kommt in diese Liste.

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

### Service Defaults

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

Das ist eine Kurzform der Service Defaults eines Aspire-Projekts. Der App-Host beobachtet `/health` der API, des Anmeldeservers und der Web-App; `MapDefaultEndpoints` stellt es bereit.

### Migrationen

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

Der Host wendet die Migrationen mit Wiederholungen an, seedet und beendet sich; die anderen Dienste starten danach. Siehe [Migrationen und Seeding](../fundamentals/persistence.md#migrationen-und-seed).

### API und Anmeldeserver

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

Die Audience `shop_api` muss der App-Name aus dem App-Host mit `_api` dahinter sein; mit einem anderen Wert lehnt die API jeden Aufruf der Web-App mit 401 ab.

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

`AddCoworkeeDatabaseConfiguration` legt die in der App geänderten Einstellungen über die Konfiguration, siehe [typisierte App-Einstellungen](../modules/settings.md#typisierte-app-einstellungen).

### Web-App

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

Der Client rendert nur im Browser (`prerender: false`), wie in einer generierten App; Stylesheets und Skript von MudBlazor kommen aus den Paketen.

### Blazor-Client

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

Der Router nimmt die Seiten von `Coworkee.Client.Blazor` (Konto, Administration) neben die eigenen der App, und `CoworkeeLayout` ist die Hülle mit Menü, Suche und Benutzermenü.

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

### App-Host

Ersetze den Inhalt von `src/Shop.AppHost/Program.cs`:

```csharp title="src/Shop.AppHost/Program.cs"
var builder = DistributedApplication.CreateBuilder(args);
var app = builder.AddCoworkeeApp("shop");

app.AddMigrations<Projects.Shop_Migrations>();
app.AddAuthServer<Projects.Shop_Auth>();
app.AddApi<Projects.Shop_Api>();
app.AddWeb<Projects.Shop_Web>();

builder.Build().Run();
```

`AddCoworkeeApp` legt die Container an, die die Pakete brauchen (Postgres, Redis, Mailpit), und gibt jedem Dienst die Connection Strings, Adressen und Anmelde-Clients, die er braucht, siehe [Aspire-App-Host](../hosting/aspire.md). Deshalb braucht keiner der Hosts einen Eintrag in seiner `appsettings.json`; die Dateien der Vorlagen bleiben, wie sie sind.

## 6. Die erste Migration

```bash
dotnet ef migrations add Initial --project src/Shop.Infrastructure --startup-project src/Shop.Infrastructure
```

Die Migration landet in `src/Shop.Infrastructure/Migrations` und legt die Tabellen aller Module aus `ShopDatabaseModule` an, darunter `app.Orders`. Nach jeder späteren Modelländerung kommt die nächste auf dieselbe Weise dazu, siehe [Persistenz](../fundamentals/persistence.md).

## 7. Bauen und starten

```bash
dotnet build Shop.slnx
dotnet run --project src/Shop.AppHost
```

Der Build endet ohne Fehler. Er zeigt Warnungen `WASM0001` zu SQLite-Funktionen im WebAssembly-Client; eine generierte App zeigt sie ebenso, sie schaden nicht.

Mit laufendem Docker gibt die Konsole die Adresse des Aspire-Dashboards aus (`https://localhost:17280` mit dem Startprofil oben). Zuerst läuft der Migrationsdienst und beendet sich; dann starten `shop-auth`, `shop-api` und `shop-web`. Öffne die Adresse von `shop-web` aus dem Dashboard, wähle **Anmelden** und melde dich mit `admin@shop.local` und dem Passwort `Admin123!` an. Die Hülle der App zeigt die Startseite, und unter **Administration** liegen Benutzer, Rollen, Einstellungen und die anderen Seiten der Pakete.

## In Rider oder Visual Studio

Dieselben Schritte gehen auch in den Dialogen der IDE. Die Menüs heißen je nach Version etwas anders; es kommt auf den Projekttyp und die Einstellungen an.

**Neue Solution.** Rider: **File | New Solution**, **Empty Solution** wählen, Name `Shop`. Visual Studio: **File > New > Project**, Vorlage **Blank Solution**, Name `Shop`.

**Projekte.** Rechtsklick auf die Solution, **Add > New Project** (Rider) bzw. **Add > New Project…** (Visual Studio), und lege jeweils in einem Ordner `src/<Name>` an:

- **Class Library** für `Shop.Contracts`, `Shop.Application`, `Shop.Sales`, `Shop.Infrastructure` und `Shop.ServiceDefaults`
- **Worker Service** für `Shop.Migrations`
- **ASP.NET Core Empty** (Rider: **ASP.NET Core Web Application** mit der Vorlage **Empty**) für `Shop.Api` und `Shop.Auth`
- **Blazor Web App** für `Shop.Web` mit Render-Modus **WebAssembly**, Interaktivität **Global** und ohne Beispielseiten; die IDE legt `Shop.Web.Client` mit an
- **Console App** für `Shop.AppHost`

Lösche danach die Dateien aus Schritt 1 im Solution Explorer.

**Verweise.** Rechtsklick auf ein Projekt, **Add > Reference…** (Rider) bzw. **Add > Project Reference…** (Visual Studio), und die Projekte aus Schritt 2 anhaken.

**Pakete.** Rechtsklick auf ein Projekt, **Manage NuGet Packages** (in Rider auch **Tools | NuGet | Manage NuGet Packages for Solution**), nach `Coworkee.` suchen, Version `0.0.1.64` wählen und die Pakete aus Schritt 3 in ihre Projekte installieren. Im Fenster für die ganze Solution kannst du für ein Paket mehrere Projekte anhaken.

**Projektdateien.** Rider: Rechtsklick auf das Projekt, **Edit > Edit 'Shop.AppHost.csproj'**. Visual Studio: Doppelklick auf das Projekt. Nimm die Änderungen aus Schritt 4 vor und lege `Properties/launchSettings.json` im App-Host an.

**Code.** Lege die Dateien aus Schritt 5 mit **Add > Class** oder **Add > File** an und ersetze den Inhalt.

**Migration.** Führe den Befehl aus Schritt 6 im Terminal der IDE aus (Rider **View | Tool Windows | Terminal**, Visual Studio **View > Terminal**), im Ordner der Solution.

**Starten.** Mach `Shop.AppHost` zum Startprojekt (Visual Studio: Rechtsklick, **Set as Startup Project**; Rider: in den Run Configurations `Shop.AppHost: https` wählen) und starte es. Der Browser öffnet das Aspire-Dashboard.

## Was die CLI dazugibt

Eine generierte App hat mehr als dieses Minimum; alles davon kannst du später von Hand ergänzen:

- `Directory.Build.props` (Warnungen als Fehler, Code-Stil) und `Directory.Packages.props` mit einer Version für alle Pakete, eine `global.json`
- die Texte der App als `Localization/<Kultur>.json` in den Contracts, eine Theme-Klasse, Navigation, Dialogoptionen
- Demo-Benutzer und -Rollen mit ihren Berechtigungen (`DemoUsers.cs`, `DemoSeed.cs`, `LOGIN.md`), die Beispielmodule
- als Optionen gewählte Feature-Module (Benachrichtigungen, Chat, KI-Assistent, Berichte, Backup, …), jedes ein Paket plus eine Zeile im Datenbankmodul
- Testprojekte, `AGENTS.md` für Coding-Agents, ein CI-Workflow

[Kommandozeile](cli.md) listet die Optionen, [Aufbau der Solution](solution-structure.md) zeigt, was eine generierte App enthält.

## Weiter

[Das erste Feature](first-feature.md) baut eine Entity mit Berechtigungen, einem Command, einem Endpunkt, einem OData-Set und einer Blazor-Seite.
