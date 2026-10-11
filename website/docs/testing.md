# Testing

`Coworkee.Testing` has what the tests of the packages and of both apps use.

## API tests against a real database

```csharp
[assembly: AssemblyFixture(typeof(ApiFixture))]

public sealed class ApiFixture : DatabaseFixture
{
    public DatabaseWebApplicationFactory<Program> Factory { get; private set; } = null!;

    public override string Provider => MyAppInfrastructureModule.DatabaseProvider;

    protected override string[] SchemasToExclude => ["hangfire"];

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        Factory = new DatabaseWebApplicationFactory<Program>(this, "myapp");
        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MyAppDbContext>().Database.MigrateAsync();
    }
}
```

`DatabaseFixture` starts a Postgres or SQL Server container, or creates a SQLite file, per test run; `ResetAsync()` empties the tables between tests. The factory hands connection string and provider to the app. Without an override `Provider` comes from the environment variable `COWORKEE_TEST_DATABASE` (`Postgres`, `SqlServer`, `Sqlite`; default Postgres), so the tests of the Coworkee packages run against every provider: `COWORKEE_TEST_DATABASE=SqlServer dotnet test`. `AddTestAuthentication()` replaces sign-in with a header, so a test acts as any user:

```csharp
var admin = api.As(setup.AdminUserId, setup.TenantId);
(await admin.PostAsJsonAsync("/api/v1/brands", new AddEditBrandRequest { Name = "Acme" }, ct)).EnsureSuccessStatusCode();
```

## Blazor components

Pages and components are tested with bUnit. Fake the API clients and `IODataClient`; the table, dialogs and permission gates render as in the browser.

```csharp
_odata.With("Brands", new BrandDto(id, "Acme", "Tools", 19));
var page = Render<Brands>();
page.WaitForAssertion(() => page.Markup.ShouldContain("Acme"));
```

## The whole system

`Aspire.Hosting.Testing` starts the real app host with ephemeral infrastructure. The template signs in through the BFF, calls the API, receives a mail in Mailpit and listens to SignalR; Sharemee runs a browser journey with Playwright and keeps screenshots when `SHAREME_E2E_SCREENSHOTS` is set.

```csharp
var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.MyApp_AppHost>(
    [$"--{CoworkeeAppExtensions.EphemeralSetting}=true"], ct);
```
