# Testing

`Coworkee.Testing` has what the tests of the Coworkee packages and of generated apps use: a database per test run, a web application factory and a sign-in by header. A new app comes with four test projects: `MyApp.Api.Tests` (the API against a real database), `MyApp.Web.Client.Tests` (bUnit), `MyApp.Migrations.Tests` and `MyApp.AppHost.Tests`.

## API tests against a real database

The template's fixture, shortened:

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

`DatabaseFixture` starts a Postgres or SQL Server container, or creates a SQLite file, per test run. Without an override `Provider` comes from the environment variable `COWORKEE_TEST_DATABASE` (`Postgres`, `SqlServer`, `Sqlite`; default Postgres), so the tests of the Coworkee packages run against every provider: `COWORKEE_TEST_DATABASE=SqlServer dotnet test`. `ResetAsync()` empties every table between tests except the migration history, the schemas of `SchemasToExclude` and the tables of `TablesToKeep`. The template's `ResetAllAsync()` also clears what the app caches (system state, permissions, settings), and `SetupAsync()` runs the setup and returns the first administrator and organisation. `DatabaseWebApplicationFactory<TProgram>(fixture, connectionStringName)` hands the connection string and the provider to the app.

`AddTestAuthentication()` replaces sign-in with a header, so a test acts as any user:

```csharp
var setup = await api.SetupAsync();
var admin = api.As(setup.AdminUserId, setup.TenantId);
(await admin.PostAsJsonAsync("/api/v1/brands", new AddEditBrandRequest { Name = "Acme" }, ct)).EnsureSuccessStatusCode();
```

The header is `Authorization: Test {user};{tenant};{exp}`; `client.AsUser(userId, tenantId)` sets the first two parts. Further parts are claims of their own (`Test {user};{tenant};;sid=abc;impersonator={id}`), e.g. a session id or an impersonation. For a new feature, prove that a user without the permission gets `403` and that another organisation sees nothing.

Two more helpers: `TestCurrentUser`, an `ICurrentUser` with settable user, organisation and roles for tests without HTTP, and `CapturedLogs`, a logger provider that keeps every record as one line with its values and scopes (`builder.Logging.AddProvider(logs)`), e.g. to check that nothing secret is logged.

### Brokers and search in API tests

An app with `--event-bus rabbitMq` needs no broker in its API tests: the template's `ApiFactory` sets `Coworkee:EventBus:RabbitMq:Enabled` (default `true`) to `false`, so integration events go from the outbox straight to the handlers of the app, as without a broker. The Elasticsearch index connects only when it is used; tests that search start a container and pass its address as the `elasticsearch` connection string.

```csharp
builder.UseSetting("Coworkee:EventBus:RabbitMq:Enabled", "false");
```

## Blazor components

Pages and components are tested with bUnit. Fake the API clients and `IODataClient`; the table, dialogs and permission gates render as in the browser.

```csharp
_odata.With("Brands", new BrandDto(id, "Acme", "Tools", 19));
var page = Render<Brands>();
page.WaitForAssertion(() => page.Markup.ShouldContain("Acme"));
```

## Migrations

`MyApp.Migrations.Tests` fails while the model has changes without a migration (`HasPendingModelChanges()`, the auth tables included) and checks that the migrations host exits with an error code when the database cannot be reached.

## The whole system

`Aspire.Hosting.Testing` starts the real app host with ephemeral infrastructure (no data volumes). The template's tests wait until the api is healthy after the migrations and answers `/api/v1/system/info`, and until the web app and the auth server serve (`/bff/user`, `/.well-known/openid-configuration`); they run one at a time. Sharemee runs a browser journey with Playwright on top and keeps screenshots when `SHAREME_E2E_SCREENSHOTS` is set.

```csharp
var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.MyApp_AppHost>(
    [$"--{CoworkeeAppExtensions.EphemeralSetting}=true"], ct);
```
