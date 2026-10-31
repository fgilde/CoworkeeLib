# Tests

`Coworkee.Testing` enthält, was die Tests der Coworkee-Pakete und generierter Apps nutzen: eine Datenbank pro Testlauf, eine Web-Application-Factory und eine Anmeldung per Header. Eine neue App bringt vier Testprojekte mit: `MyApp.Api.Tests` (die API gegen eine echte Datenbank), `MyApp.Web.Client.Tests` (bUnit), `MyApp.Migrations.Tests` und `MyApp.AppHost.Tests`.

## API-Tests gegen eine echte Datenbank

Die Fixture der Vorlage, gekürzt:

```csharp
[assembly: AssemblyFixture(typeof(ApiFixture))]

public sealed class ApiFixture : DatabaseFixture
{
    public ApiFactory Factory { get; private set; } = null!;

    public override string Provider => MyAppInfrastructureModule.DatabaseProvider;

    protected override string[] SchemasToExclude => ["hangfire"];

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        Factory = new ApiFactory(this);
        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MyAppDbContext>().Database.MigrateAsync();
    }

    public HttpClient As(Guid userId, Guid tenantId) => Factory.CreateClient().AsUser(userId, tenantId);
}

public sealed class ApiFactory(DatabaseFixture database) : DatabaseWebApplicationFactory<Program>(database, MyAppInfrastructureModule.ConnectionStringName)
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => services.AddTestAuthentication());
    }
}
```

`DatabaseFixture` startet pro Testlauf einen Postgres- oder SQL-Server-Container oder legt eine SQLite-Datei an. Ohne Override kommt `Provider` aus der Umgebungsvariable `COWORKEE_TEST_DATABASE` (`Postgres`, `SqlServer`, `Sqlite`; Standard Postgres), so laufen die Tests der Coworkee-Pakete gegen jeden Provider: `COWORKEE_TEST_DATABASE=SqlServer dotnet test`. `ResetAsync()` leert zwischen den Tests jede Tabelle außer der Migrationshistorie, den Schemas aus `SchemasToExclude` und den Tabellen aus `TablesToKeep`. `ResetAllAsync()` der Vorlage leert zusätzlich, was die App zwischenspeichert (Systemzustand, Berechtigungen, Einstellungen), und `SetupAsync()` führt die Einrichtung aus und gibt den ersten Administrator und die Organisation zurück. `DatabaseWebApplicationFactory<TProgram>(fixture, connectionStringName)` reicht Connection-String und Provider an die App weiter.

`AddTestAuthentication()` ersetzt die Anmeldung durch einen Header, damit ein Test als beliebiger Benutzer handelt:

```csharp
var setup = await api.SetupAsync();
var admin = api.As(setup.AdminUserId, setup.TenantId);
(await admin.PostAsJsonAsync("/api/v1/brands", new AddEditBrandRequest { Name = "Acme" }, ct)).EnsureSuccessStatusCode();
```

Der Header lautet `Authorization: Test {user};{tenant};{exp}`; `client.AsUser(userId, tenantId)` setzt die ersten beiden Teile. Weitere Teile sind eigene Claims (`Test {user};{tenant};;sid=abc;impersonator={id}`), etwa eine Sitzungs-ID oder ein Anmelden als anderer Benutzer. Für eine neue Funktion belegen die Tests, dass ein Benutzer ohne die Berechtigung `403` bekommt und eine andere Organisation nichts sieht.

Zwei weitere Helfer: `TestCurrentUser`, ein `ICurrentUser` mit setzbarem Benutzer, Organisation und Rollen für Tests ohne HTTP, und `CapturedLogs`, ein Logger-Provider, der jeden Eintrag als eine Zeile mit seinen Werten und Scopes behält (`builder.Logging.AddProvider(logs)`), etwa um zu prüfen, dass nichts Geheimes geloggt wird.

### Broker und Suche in API-Tests

Eine App mit `--event-bus rabbitMq` braucht in ihren API-Tests keinen Broker: Die `ApiFactory` der Vorlage setzt `Coworkee:EventBus:RabbitMq:Enabled` (Standard `true`) auf `false`, damit Integration-Events wie ohne Broker direkt aus der Outbox an die Handler der App gehen. Der Elasticsearch-Index verbindet sich erst bei Benutzung; Tests, die suchen, starten einen Container und übergeben seine Adresse als Connection-String `elasticsearch`.

```csharp
builder.UseSetting("Coworkee:EventBus:RabbitMq:Enabled", "false");
```

## Blazor-Komponenten

Seiten und Komponenten werden mit bUnit getestet. API-Clients und `IODataClient` werden ersetzt; Tabelle, Dialoge und Berechtigungs-Gates rendern wie im Browser.

```csharp
_odata.With("Brands", new BrandDto(id, "Acme", "Tools", 19));
var page = Render<Brands>();
page.WaitForAssertion(() => page.Markup.ShouldContain("Acme"));
```

## Migrationen

`MyApp.Migrations.Tests` schlägt fehl, solange das Modell Änderungen ohne Migration hat (`HasPendingModelChanges()`, die Auth-Tabellen eingeschlossen), und prüft, dass der Migrations-Host mit einem Fehlercode endet, wenn die Datenbank nicht erreichbar ist.

## Das ganze System

`Aspire.Hosting.Testing` startet den echten AppHost mit flüchtiger Infrastruktur (ohne Daten-Volumes). Die Tests der Vorlage warten, bis die API nach den Migrationen gesund ist und `/api/v1/system/info` beantwortet und bis Web-App und Auth-Server antworten (`/bff/user`, `/.well-known/openid-configuration`); sie laufen nacheinander. Sharemee spielt darüber hinaus eine Browser-Reise mit Playwright durch und behält Screenshots, wenn `SHAREME_E2E_SCREENSHOTS` gesetzt ist.

```csharp
var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.MyApp_AppHost>(
    [$"--{CoworkeeAppExtensions.EphemeralSetting}=true"], ct);
```
