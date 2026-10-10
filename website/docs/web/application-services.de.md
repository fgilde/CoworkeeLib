# Anwendungsdienste

Ein Anwendungsdienst ist ein Interface in den Contracts. Der Server stellt seine Implementierung per Konvention als HTTP-Endpunkte bereit, und der Blazor-Client bekommt eine Implementierung desselben Interfaces, die diese Endpunkte aufruft. Keine Seite schreibt HTTP-Code: Ein Source Generator, der mit `Coworkee.AspNetCore` und `Coworkee.Client.Blazor` kommt, schreibt Endpunkte und Proxies beim Build (keine Reflection zur Laufzeit, Trimming und AOT in WebAssembly funktionieren also).

```csharp title="MyApp.Contracts/Documents/IDocumentTypeAppService.cs"
public interface IDocumentTypeAppService : IApplicationService
{
    Task<DocumentTypeDto> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<DocumentTypeDto> CreateAsync(AddEditDocumentTypeRequest input, CancellationToken cancellationToken = default);

    Task<DocumentTypeDto> UpdateAsync(Guid id, AddEditDocumentTypeRequest input, CancellationToken cancellationToken = default);

    [ServiceOperation(Method = "POST", Route = "delete")]
    Task DeleteAsync(IdsRequest request, CancellationToken cancellationToken = default);
}
```

```csharp title="MyApp.Documents/Features/DocumentTypes/DocumentTypeAppService.cs"
internal sealed class DocumentTypeAppService(IDispatcher dispatcher, CoworkeeDbContext db) : IDocumentTypeAppService
{
    [RequiresPermission(DocumentPermissions.Types.View)]
    public async Task<DocumentTypeDto> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        (await db.Set<DocumentType>().AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, cancellationToken))?.ToDto()
        ?? throw new ErrorException(DocumentErrors.TypeNotFound);

    public Task<DocumentTypeDto> CreateAsync(AddEditDocumentTypeRequest input, CancellationToken cancellationToken = default) =>
        dispatcher.SendAsync(new AddEditDocumentTypeCommand(null, input), cancellationToken).OrThrow();
    // …
}
```

```csharp title="MyApp.Web.Client/Pages/Documents/DocumentTypes.razor.cs"
[Inject] private IDocumentTypeAppService Types { get; set; } = null!;
```

Mehr braucht es nicht: kein Endpunkt-Mapping, keine Registrierung, keine Client-Klasse.

- **Server**: `AddCoworkee` registriert jede Implementierung aus den Assemblies der Module (scoped, außer das Interface ist schon registriert), und `UseCoworkee` mappt sie. Hosts ohne Module rufen `services.AddCoworkeeApplicationServices(assembly)` und `app.MapCoworkeeApplicationServices()` auf.
- **Client**: `AddCoworkeeClient` registriert für jeden Anwendungsdienst, den das Client-Projekt sieht, einen Proxy als typisierten `HttpClient` mit der Basisadresse der App. Proxies erben von `ApiClientBase`, laufen also mit CSRF-Header über das BFF und werfen `ApiException` mit den Problem Details.

## Konventionen

Der Dienstname ist der Interface-Name ohne `I` und ohne `AppService`, `ApplicationService` oder `Service`. Die Route ist `/api/v1/` plus der Plural in Kebab-Case, das OpenAPI-Tag der Plural in Worten: Aus `IDocumentTypeAppService` wird `/api/v1/document-types` mit dem Tag "Document types".

Der Methodenname (ohne `Async`) bestimmt die HTTP-Methode:

| Name beginnt mit | Methode | Routenteil aus dem Namen |
|---|---|---|
| `Get` | GET | Rest des Namens; keiner bei `Get`, `GetList`, `GetAll`, `GetById` |
| `Find`, `List`, `Search`, `Query`, `Count` | GET | der ganze Name, etwa `search` |
| `Create`, `Add`, `Insert` | POST | Rest des Namens |
| `Update` | PUT | Rest des Namens |
| `Patch` | PATCH | Rest des Namens |
| `Delete`, `Remove` | DELETE | Rest des Namens |
| alles andere | POST | der ganze Name, etwa `archive` |

Parameter werden so gebunden:

| Parameter | Kommt aus |
|---|---|
| `CancellationToken` | dem Request |
| in der Route genannt, oder `id` eines einfachen Typs | der Route, `{id:guid}` für eine Guid (`:int`, `:long` ebenso) |
| einfacher Typ (Zahlen, string, bool, Guid, Datumswerte, Enums) oder eine Liste davon | dem Query-String |
| andere Typen bei POST, PUT, PATCH | dem JSON-Body (nur einer pro Methode) |
| andere Typen bei GET, DELETE | dem Query-String, ein Wert pro Property (`[AsParameters]`) |

`UpdateAsync(Guid id, AddEditDocumentTypeRequest input)` ist also `PUT /api/v1/document-types/{id}` mit dem Request als Body, und `ArchiveAsync(Guid id)` ist `POST /api/v1/document-types/{id}/archive`. Methoden geben `Task`, `Task<T>`, `ValueTask` oder `ValueTask<T>` zurück: Ein Wert kommt als `200` mit JSON zurück, kein Wert als `204`.

## Konventionen überschreiben

```csharp
[ApplicationService(Route = "/api/v2/types", Tag = "Types")]
public interface IDocumentTypeAppService : IApplicationService
{
    [ServiceOperation(Method = "PUT", Route = "by-name/{name}")]
    Task<DocumentTypeDto> RenameAsync(string name, [ServiceParameter(ParameterSource.Query, Name = "to")] string newName);

    [ServiceOperation(Ignore = true)]
    Task WarmUpAsync();
}
```

`Route` von `[ServiceOperation]` ist relativ zum Dienst; Platzhalter `{name}` binden die gleichnamigen Parameter. Eine ignorierte Methode bekommt keinen Endpunkt und ihr Proxy wirft `NotSupportedException`; das Interface kann so auch Methoden für Aufrufe im Prozess enthalten.

Der Build bricht mit einer klaren Meldung ab, wenn ein Contract über HTTP nicht funktionieren kann: `CWK001` für Member, die keine Methoden mit Task-Rückgabe sind (oder generisch sind oder `ref`/`out` haben), `CWK002` für zwei Body-Parameter oder einen Routen-Platzhalter ohne Parameter, `CWK003` für zwei Methoden mit gleicher Methode und Route.

## Berechtigungen, Validierung, Fehler

Jeder Endpunkt eines Anwendungsdienstes verlangt einen angemeldeten Benutzer und führt in dieser Reihenfolge aus:

1. `[RequiresPermission]` an der implementierenden Klasse oder Methode: `401` ohne Benutzer, `403` ohne die Berechtigung.
2. FluentValidation: Jeder für einen Argumenttyp registrierte Validator (`IValidator<AddEditDocumentTypeRequest>`) läuft; Fehler kommen als `400` mit `errors` pro Feld zurück.
3. Die Methode.
4. Die Unit of Work (`IUnitOfWork.SaveChangesAsync`) für alles außer GET; Implementierungen, die den `DbContext` direkt ändern, müssen also nicht speichern.

Fehler sind Exceptions: `ErrorException` trägt einen `Error` und wird zu denselben Problem Details wie ein fehlgeschlagenes `Result` (siehe [HTTP-APIs](http-api.md)). Methoden, die Arbeit an den Dispatcher geben, machen aus einem fehlgeschlagenen Result mit `OrThrow()` eine solche Exception; Commands behalten ihre eigenen Berechtigungsprüfungen, Validatoren und KI-Tool-Beschreibungen.

## Einzelne Dienste konfigurieren

Zuerst die Vorgaben für alle Dienste, dann die Einstellungen einzelner.

```csharp title="Server"
services.Configure<ApplicationServiceOptions>(options => options
    .ConfigureAll(service => service.Conventions.Add(group => group.RequireCoworkeeRateLimit(CoworkeeRateLimitOptions.Upload)))
    .Configure<IDocumentTypeAppService>(service =>
    {
        service.Route = "/api/v2/document-types";
        service.RequireAuthorization = false;
    }));
```

| `ApplicationServiceDescriptor` | |
|---|---|
| `Route`, `Tag` | aus den Konventionen und `[ApplicationService]` |
| `Enabled` | `false` hält den Dienst aus der API heraus; für Aufrufe im Prozess bleibt er registriert |
| `RequireAuthorization` | standardmäßig `true` |
| `Conventions` | werden auf die Endpunktgruppe des Dienstes angewendet |
| `Properties` | Einstellungen, die andere Features an einen Dienst hängen |

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

Die generierten Endpunkte sind gewöhnliche Minimal-APIs im OpenAPI-Dokument, die [SDKs](sdk.md) für andere Sprachen erfassen sie also wie gewohnt. Proxies registrieren sich, wenn ihre Assembly geladen wird: die der Client-App immer; die in einer Klassenbibliothek generierten nur, wenn die Bibliothek geladen ist, bevor `AddCoworkeeClient` läuft.
