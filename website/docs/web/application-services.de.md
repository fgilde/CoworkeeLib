# Anwendungsdienste

Ein Anwendungsdienst ist ein Interface in den Contracts. Der Server stellt seine Implementierung per Konvention als HTTP-Endpunkte bereit, und der Blazor-Client bekommt eine Implementierung desselben Interfaces, die diese Endpunkte aufruft. Keine Seite schreibt HTTP-Code: Ein Source Generator, der mit `Coworkee.AspNetCore` und `Coworkee.Client.Blazor` kommt, schreibt Endpunkte und Client-Proxies beim Build (keine Reflection zur Laufzeit, Trimming und AOT in WebAssembly funktionieren also).

**Wozu**: Ein Interface ist der ganze API-Vertrag. Die Seite ruft `Orders.CancelAsync(id)` wie einen lokalen Dienst, der Server bekommt eine gewöhnliche Minimal-API mit Berechtigungen, Validierung und Unit of Work, und OpenAPI-Dokument und SDKs sehen dieselben Endpunkte. **Im Vergleich** zu [handgeschriebenen Endpunkten](http-api.md) und dem [Dispatcher](../fundamentals/messaging.md): Nimm einen Dienst für die Operationen, die ein Client aufruft; Commands bleiben für Arbeit, die auch aus Jobs, Importen oder dem KI-Assistenten läuft (eine Dienstmethode kann einfach den Command senden).

Das Template hat zwei: `IDocumentTypeAppService` (Dokumente) und `ISalesOrderAppService` (Vertrieb). Diese Seite baut den zweiten Schritt für Schritt auf.

## Schritt 1: das Interface in den Contracts

```csharp title="MyApp.Contracts/Sales/ISalesOrderAppService.cs"
public interface ISalesOrderAppService : IApplicationService
{
    Task<SalesOrderDto> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<SalesOrderDto> CreateAsync(AddEditSalesOrderRequest input, CancellationToken cancellationToken = default);

    Task<SalesOrderDto> UpdateAsync(Guid id, AddEditSalesOrderRequest input, CancellationToken cancellationToken = default);

    Task CancelAsync(Guid id, CancellationToken cancellationToken = default);

    [ServiceOperation(Method = "POST", Route = "delete")]
    Task DeleteAsync(IdsRequest request, CancellationToken cancellationToken = default);
}
```

`IApplicationService` (Paket `Coworkee.Contracts`) markiert es. Erlaubt sind nur Methoden, die `Task`, `Task<T>`, `ValueTask` oder `ValueTask<T>` zurückgeben; sonst meldet sich der Build.

## Schritt 2: die Implementierung im Modul

```csharp title="MyApp.Sales/Features/Orders/SalesOrderAppService.cs"
internal sealed class SalesOrderAppService(CoworkeeDbContext db, TimeProvider clock) : ISalesOrderAppService
{
    [RequiresPermission(SalesPermissions.Orders.View)]
    public async Task<SalesOrderDto> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        (await db.Set<SalesOrder>().AsNoTracking().Include(o => o.Customer).SingleOrDefaultAsync(o => o.Id == id, cancellationToken))?.ToDto()
        ?? throw new ErrorException(SalesErrors.OrderNotFound);

    [RequiresPermission(SalesPermissions.Orders.Edit)]
    public async Task CancelAsync(Guid id, CancellationToken cancellationToken = default) =>
        (await FindAsync(id, cancellationToken)).Status = SalesOrderStatus.Cancelled;
    // CreateAsync, UpdateAsync, DeleteAsync …
}
```

```csharp title="MyApp.Sales/Features/Orders/AddEditSalesOrderValidator.cs"
internal sealed class AddEditSalesOrderValidator : AbstractValidator<AddEditSalesOrderRequest>
{
    public AddEditSalesOrderValidator()
    {
        RuleFor(o => o.CustomerId).NotEmpty();
        RuleFor(o => o.Region).NotEmpty().MaximumLength(50);
        RuleFor(o => o.Items).GreaterThan(0);
    }
}
```

Es gibt keine Registrierung und kein Mapping: `AddCoworkee` registriert jede Implementierung aus den Assemblies der Module (scoped, außer das Interface ist schon registriert), und `UseCoworkee` mappt sie. Die Implementierung ändert den `DbContext` und speichert nicht; das macht der Endpunkt (Schritt 4). Der `DocumentTypeAppService` des Templates zeigt den anderen Stil: Jede Methode sendet einen Command über den Dispatcher und macht aus einem fehlgeschlagenen Result mit `OrThrow()` eine Exception.

Hosts ohne Module rufen `services.AddCoworkeeApplicationServices(assembly)` und `app.MapCoworkeeApplicationServices()` auf.

## Schritt 3: was generiert wird

Der Dienstname ist `SalesOrder` (Interface-Name ohne `I` und `AppService`), die Route also `/api/v1/sales-orders` und das OpenAPI-Tag "Sales orders":

| Methode | Endpunkt | Antwort |
|---|---|---|
| `GetAsync(Guid id)` | `GET /api/v1/sales-orders/{id}` | `200` mit dem Auftrag, `404` |
| `CreateAsync(AddEditSalesOrderRequest input)` | `POST /api/v1/sales-orders` mit dem Request als Body | `200` mit dem Auftrag, `400` |
| `UpdateAsync(Guid id, AddEditSalesOrderRequest input)` | `PUT /api/v1/sales-orders/{id}` | `200`, `400`, `404` |
| `CancelAsync(Guid id)` | `POST /api/v1/sales-orders/{id}/cancel` | `204` |
| `DeleteAsync(IdsRequest request)` | `POST /api/v1/sales-orders/delete` (aus `[ServiceOperation]`) | `204` |

Auf dem Server schreibt der Generator pro Methode eine Minimal-API in `CoworkeeApplicationServiceEndpoints.g.cs`, etwa:

```csharp
endpoints.MapMethods("{id:guid}/cancel", ["POST"],
    static async ([FromRoute(Name = "id")] Guid id, CancellationToken cancellationToken, [FromServices] ISalesOrderAppService service) =>
    {
        await service.CancelAsync(id, cancellationToken);
        return TypedResults.NoContent();
    });
```

Im Client schreibt er `CoworkeeServiceProxies.g.cs`, eine Klasse pro Interface:

```csharp
internal sealed class SalesOrderAppServiceProxy(HttpClient http) : ApiClientBase(http), ISalesOrderAppService
{
    Task ISalesOrderAppService.CancelAsync(Guid id, CancellationToken cancellationToken)
        => SendAsync(HttpMethod.Post, "api/v1/sales-orders/" + ServiceProxyUrl.Segment(id) + "/cancel", null, cancellationToken);
    // …
}
```

Die Dateien sieht man in der IDE unter *Dependencies → Analyzers → Coworkee.SourceGenerators*, oder man setzt `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>` und findet sie unter `obj/…/generated/Coworkee.SourceGenerators/`. Die Endpunkte stehen wie alle anderen im OpenAPI-Dokument (`/openapi/v1.json`); die [SDKs](sdk.md) für TypeScript und andere Sprachen bekommen sie also auch.

## Schritt 4: Berechtigungen, Validierung, Fehler, Speichern

Jeder Endpunkt eines Anwendungsdienstes verlangt einen angemeldeten Benutzer und führt in dieser Reihenfolge aus:

1. `[RequiresPermission]` an der implementierenden Klasse oder Methode: `401` ohne Benutzer, `403` ohne die Berechtigung.
2. FluentValidation: Jeder für einen Argumenttyp registrierte Validator (`IValidator<AddEditSalesOrderRequest>`, registriert von `AddMessagingFromAssembly`) läuft; Fehler kommen als `400` mit `errors` pro Feld zurück.
3. Die Methode.
4. Die Unit of Work (`IUnitOfWork.SaveChangesAsync`) für alles außer GET; Implementierungen, die den `DbContext` direkt ändern, müssen also nicht speichern.

Fehler sind Exceptions: `ErrorException` trägt einen `Error` und wird zu denselben Problem Details wie ein fehlgeschlagenes `Result` (siehe [HTTP-APIs](http-api.md)); `SalesErrors.OrderNotFound` ist ein `404`, `SalesErrors.UnknownCustomer` ein `400` am Feld `CustomerId`.

## Schritt 5: der Client

`AddCoworkeeClient` registriert für jeden Anwendungsdienst, den das Client-Projekt sieht, einen Proxy als typisierten `HttpClient` mit der Basisadresse der App. Injiziere das Interface:

```csharp title="MyApp.Web.Client/Pages/Sales/SalesOrders.razor.cs"
[Inject] private ISalesOrderAppService Orders { get; set; } = null!;

private Task CancelAsync(SalesOrderDto order) => Snackbar.RunAsync(() => Orders.CancelAsync(order.Id));

private async Task EditAsync(Guid? id, AddEditSalesOrderRequest model)
{
    if (await Dialogs.ShowEditAsync(L[id is null ? "New order" : "Edit order"], model,
            saved => id is { } existing ? Orders.UpdateAsync(existing, saved) : Orders.CreateAsync(saved), Configure))
    {
        Snackbar.Add(L["Order saved"], Severity.Success);
    }
}
```

Proxies erben von `ApiClientBase`: Sie laufen mit CSRF-Header über das BFF und werfen `ApiException` mit den Problem Details, die `Snackbar.RunAsync` und der Bearbeiten-Dialog anzeigen. Die Tabelle der Seite liest über OData; die Aufträge sind eine [Client-Entity](../ui/client-entities.md), deren lokale Kopie sich direkt nach so einem Schreiben aktualisiert.

In Tests ersetzt man das Interface: `Services.AddSingleton(Substitute.For<ISalesOrderAppService>())`.

## Konventionen

Der Dienstname ist der Interface-Name ohne `I` und ohne `AppService`, `ApplicationService` oder `Service`. Die Route ist `/api/v1/` plus der Plural in Kebab-Case, das OpenAPI-Tag der Plural in Worten.

Der Methodenname (ohne `Async`) bestimmt die HTTP-Methode:

| Name beginnt mit | Methode | Routenteil aus dem Namen |
|---|---|---|
| `Get` | GET | Rest des Namens; keiner bei `Get`, `GetList`, `GetAll`, `GetById` |
| `Find`, `List`, `Search`, `Query`, `Count` | GET | der ganze Name, etwa `search` |
| `Create`, `Add`, `Insert` | POST | Rest des Namens |
| `Update` | PUT | Rest des Namens |
| `Patch` | PATCH | Rest des Namens |
| `Delete`, `Remove` | DELETE | Rest des Namens |
| alles andere | POST | der ganze Name, etwa `cancel` |

Parameter werden so gebunden:

| Parameter | Kommt aus |
|---|---|
| `CancellationToken` | dem Request |
| in der Route genannt, oder `id` eines einfachen Typs | der Route, `{id:guid}` für eine Guid (`:int`, `:long` ebenso) |
| einfacher Typ (Zahlen, string, bool, Guid, Datumswerte, Enums) oder eine Liste davon | dem Query-String |
| andere Typen bei POST, PUT, PATCH | dem JSON-Body (nur einer pro Methode) |
| andere Typen bei GET, DELETE | dem Query-String, ein Wert pro Property (`[AsParameters]`) |

Methoden geben `Task`, `Task<T>`, `ValueTask` oder `ValueTask<T>` zurück: Ein Wert kommt als `200` mit JSON zurück, kein Wert als `204`.

## Konventionen überschreiben

```csharp
[ApplicationService(Route = "/api/v2/orders", Tag = "Orders")]
public interface ISalesOrderAppService : IApplicationService
{
    [ServiceOperation(Method = "PUT", Route = "by-number/{number}")]
    Task<SalesOrderDto> RenumberAsync(string number, [ServiceParameter(ParameterSource.Query, Name = "to")] string newNumber);

    [ServiceOperation(Ignore = true)]
    Task WarmUpAsync();
}
```

`Route` von `[ServiceOperation]` ist relativ zum Dienst; Platzhalter `{name}` binden die gleichnamigen Parameter. Eine ignorierte Methode bekommt keinen Endpunkt und ihr Proxy wirft `NotSupportedException`; das Interface kann so auch Methoden für Aufrufe im Prozess enthalten.

Der Build bricht mit einer klaren Meldung ab, wenn ein Contract über HTTP nicht funktionieren kann: `CWK001` für Member, die keine Methoden mit Task-Rückgabe sind (oder generisch sind oder `ref`/`out` haben), `CWK002` für zwei Body-Parameter oder einen Routen-Platzhalter ohne Parameter, `CWK003` für zwei Methoden mit gleicher Methode und Route.

## Einzelne Dienste konfigurieren

Zuerst die Vorgaben für alle Dienste, dann die Einstellungen einzelner.

```csharp title="Server"
services.Configure<ApplicationServiceOptions>(options => options
    .ConfigureAll(service => service.Conventions.Add(group => group.RequireCoworkeeRateLimit(CoworkeeRateLimitOptions.Upload)))
    .Configure<IDocumentTypeAppService>(service =>
    {
        service.Route = "/api/v2/document-types";
        service.RequireAuthorization = false;
    })
    .Configure<ISalesOrderAppService>(service => service.ClientEntity<SalesOrder>(settings => settings.MaxRows = 250_000)));
```

| `ApplicationServiceDescriptor` | |
|---|---|
| `Route`, `Tag` | aus den Konventionen und `[ApplicationService]` |
| `Enabled` | `false` hält den Dienst aus der API heraus; für Aufrufe im Prozess bleibt er registriert |
| `RequireAuthorization` | standardmäßig `true` |
| `Conventions` | werden auf die Endpunktgruppe des Dienstes angewendet |
| `Properties` | Einstellungen, die andere Features an einen Dienst hängen, etwa `ClientEntity<TEntity>()` aus `Coworkee.OData` ([Client-Entities](../ui/client-entities.md)) |

Ein eigenes Feature hängt Einstellungen genauso an, mit einer Erweiterungsmethode, die in `Properties` schreibt, und liest sie mit `serviceProvider.GetApplicationServices()` zurück; das listet jeden registrierten Dienst mit seinen endgültigen Einstellungen.

```csharp title="Client"
builder.Services.AddCoworkeeClient(baseAddress, options => options.ServiceProxies
    .ConfigureAll(proxy => proxy.Timeout = TimeSpan.FromMinutes(2))
    .Configure<IDocumentTypeAppService>(proxy => proxy.HttpClient.Add(client => client.AddHttpMessageHandler<AuditHandler>())));
```

| `ServiceProxyDescriptor` | |
|---|---|
| `BaseAddress`, `Timeout` | des typisierten `HttpClient` |
| `Enabled` | `false` registriert keinen Proxy |
| `HttpClient` | wird auf den `IHttpClientBuilder` des Proxys angewendet |
| `Properties` | Einstellungen, die andere Features an einen Dienst hängen |

Um einen Proxy zu ersetzen, registriert man nach `AddCoworkeeClient` eine eigene Implementierung des Interfaces oder schaltet den Proxy mit `Enabled = false` ab.

## Projekte und Pakete

Der Generator läuft in den Projekten, die `Coworkee.AspNetCore` (Endpunkte für die Implementierungen dieses Projekts) oder `Coworkee.Client.Blazor` (Proxies für die Interfaces, die es sieht) direkt referenzieren. In einem Projekt, das beide referenziert und keine Proxies braucht, setzt man `<CoworkeeServiceProxies>false</CoworkeeServiceProxies>`. Projekte, die statt der Pakete die Coworkee-Quellen referenzieren, binden den Generator selbst ein:

```xml
<ProjectReference Include="..\coworkee\src\Coworkee.SourceGenerators\Coworkee.SourceGenerators.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

Proxies registrieren sich, wenn ihre Assembly geladen wird: die der Client-App immer; die in einer Klassenbibliothek generierten nur, wenn die Bibliothek geladen ist, bevor `AddCoworkeeClient` läuft.

## Dateien des Beispiels

| Datei | |
|---|---|
| `MyApp.Contracts/Sales/ISalesOrderAppService.cs` | das Interface |
| `MyApp.Contracts/Sales/AddEditSalesOrderRequest.cs`, `SalesOrderDto.cs` | Request und Antwort |
| `MyApp.Sales/Features/Orders/SalesOrderAppService.cs` | die Implementierung |
| `MyApp.Sales/Features/Orders/AddEditSalesOrderValidator.cs` | Validierung des Requests |
| `MyApp.Web.Client/Pages/Sales/SalesOrders.razor.cs` | die Seite mit dem generierten Client |
| `tests/MyApp.Api.Tests/SalesTests.cs` | die Endpunkte über HTTP |
