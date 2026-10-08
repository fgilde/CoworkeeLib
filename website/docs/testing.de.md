# Tests

`Coworkee.Testing` enthält, was die Tests der Pakete und beider Apps nutzen.

## API-Tests gegen eine echte Datenbank

```csharp
[assembly: AssemblyFixture(typeof(ApiFixture))]

public sealed class ApiFixture : PostgresFixture
{
    public PostgresWebApplicationFactory<Program> Factory { get; private set; } = null!;

    protected override string[] SchemasToExclude => ["hangfire"];

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        Factory = new PostgresWebApplicationFactory<Program>(this, "myapp");
        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MyAppDbContext>().Database.MigrateAsync();
    }
}
```

`PostgresFixture` startet pro Testlauf einen Postgres-Container; `ResetAsync()` leert die Tabellen zwischen den Tests. `AddTestAuthentication()` ersetzt die Anmeldung durch einen Header, damit ein Test als beliebiger Benutzer handelt:

```csharp
var admin = api.As(setup.AdminUserId, setup.TenantId);
(await admin.PostAsJsonAsync("/api/v1/brands", new AddEditBrandRequest { Name = "Acme" }, ct)).EnsureSuccessStatusCode();
```

## Blazor-Komponenten

Seiten und Komponenten werden mit bUnit getestet. API-Clients und `IODataClient` werden ersetzt; Tabelle, Dialoge und Berechtigungs-Gates rendern wie im Browser.

```csharp
_odata.With("Brands", new BrandDto(id, "Acme", "Tools", 19));
var page = Render<Brands>();
page.WaitForAssertion(() => page.Markup.ShouldContain("Acme"));
```

## Das ganze System

`Aspire.Hosting.Testing` startet den echten AppHost mit flüchtiger Infrastruktur. Das Template meldet sich über den BFF an, ruft die API, empfängt eine Mail in Mailpit und hört auf SignalR; Sharemee spielt eine Browser-Reise mit Playwright durch und behält Screenshots, wenn `SHAREME_E2E_SCREENSHOTS` gesetzt ist.

```csharp
var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.MyApp_AppHost>(
    [$"--{CoworkeeAppExtensions.EphemeralSetting}=true"], ct);
```
