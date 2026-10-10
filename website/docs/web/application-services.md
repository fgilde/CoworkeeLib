# Application services

An application service is an interface in the contracts. The server exposes its implementation as HTTP endpoints by convention, and the Blazor client gets an implementation of the same interface that calls them. Neither side writes HTTP code: a source generator, shipped in `Coworkee.AspNetCore` and `Coworkee.Client.Blazor`, writes the endpoints and the proxies at build time (no reflection at runtime, so trimming and AOT in WebAssembly are fine).

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

That is all: no endpoint mapping, no registration, no client class.

- **Server**: `AddCoworkee` registers every implementation found in the assemblies of the modules (scoped, unless the interface is registered already) and `UseCoworkee` maps it. Hosts without modules call `services.AddCoworkeeApplicationServices(assembly)` and `app.MapCoworkeeApplicationServices()`.
- **Client**: `AddCoworkeeClient` registers a proxy for every application service the client project sees, as typed `HttpClient` with the base address of the app. Proxies derive from `ApiClientBase`, so they go through the BFF with the CSRF header and throw `ApiException` with the problem details.

## Conventions

The service name is the interface name without `I` and without `AppService`, `ApplicationService` or `Service`. The route is `/api/v1/` plus the plural in kebab case, the OpenAPI tag the plural in words: `IDocumentTypeAppService` becomes `/api/v1/document-types` with tag "Document types".

The method name (without `Async`) picks the HTTP method:

| Name starts with | Method | Route part from the name |
|---|---|---|
| `Get` | GET | rest of the name; none for `Get`, `GetList`, `GetAll`, `GetById` |
| `Find`, `List`, `Search`, `Query`, `Count` | GET | the whole name, for example `search` |
| `Create`, `Add`, `Insert` | POST | rest of the name |
| `Update` | PUT | rest of the name |
| `Patch` | PATCH | rest of the name |
| `Delete`, `Remove` | DELETE | rest of the name |
| anything else | POST | the whole name, for example `archive` |

Parameters bind like this:

| Parameter | Comes from |
|---|---|
| `CancellationToken` | the request |
| named in the route, or `id` of a simple type | the route, `{id:guid}` for a Guid (`:int`, `:long` likewise) |
| simple type (numbers, string, bool, Guid, dates, enums) or a list of them | the query string |
| other types with POST, PUT, PATCH | the JSON body (only one per method) |
| other types with GET, DELETE | the query string, one value per property (`[AsParameters]`) |

So `UpdateAsync(Guid id, AddEditDocumentTypeRequest input)` is `PUT /api/v1/document-types/{id}` with the request as body, and `ArchiveAsync(Guid id)` is `POST /api/v1/document-types/{id}/archive`. Methods return `Task`, `Task<T>`, `ValueTask` or `ValueTask<T>`: a value comes back as `200` with JSON, no value as `204`.

## Overriding the conventions

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

`Route` of `[ServiceOperation]` is relative to the service; `{name}` placeholders bind the parameters of the same name. An ignored method gets no endpoint and its proxy throws `NotSupportedException`, so the interface can still hold methods for calls in process.

The build fails with a clear message when a contract cannot work over HTTP: `CWK001` for members that are not methods returning a task (or generic, or with `ref`/`out`), `CWK002` for two body parameters or a route placeholder without parameter, `CWK003` for two methods with the same method and route.

## Permissions, validation, errors

Every application service endpoint requires a signed-in user and runs, in this order:

1. `[RequiresPermission]` on the implementing class or method: `401` without a user, `403` without the permission.
2. FluentValidation: every validator registered for an argument type (`IValidator<AddEditDocumentTypeRequest>`) runs; failures come back as `400` with `errors` per field.
3. The method.
4. The unit of work (`IUnitOfWork.SaveChangesAsync`) for everything but GET, so implementations that change the `DbContext` directly need no save.

Errors are exceptions: `ErrorException` carries an `Error` and becomes the same problem details as a failed `Result` (see [HTTP APIs](http-api.md)). Methods that hand work to the dispatcher turn a failed result into one with `OrThrow()`; commands keep their own permission checks, validators and AI tool descriptions.

## Configuring single services

Defaults for all services first, then the settings of single ones.

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
| `Route`, `Tag` | from the conventions and `[ApplicationService]` |
| `Enabled` | `false` keeps the service off the API; it stays registered for calls in process |
| `RequireAuthorization` | `true` by default |
| `Conventions` | applied to the endpoint group of the service |
| `Properties` | settings other features attach to a service |

```csharp title="Client"
builder.Services.AddCoworkeeClient(baseAddress, options => options.ServiceProxies
    .ConfigureAll(proxy => proxy.Timeout = TimeSpan.FromMinutes(2))
    .Configure<IDocumentTypeAppService>(proxy => proxy.HttpClient.Add(client => client.AddHttpMessageHandler<AuditHandler>())));
```

| `ServiceProxyDescriptor` | |
|---|---|
| `BaseAddress`, `Timeout` | of the typed `HttpClient` |
| `Enabled` | `false` registers no proxy |
| `HttpClient` | applied to the `IHttpClientBuilder` of the proxy |
| `Properties` | settings other features attach to a service |

To replace a proxy, register an own implementation of the interface after `AddCoworkeeClient`, or turn the proxy off with `Enabled = false`.

## Projects and packages

The generator runs in the projects that reference `Coworkee.AspNetCore` (endpoints for the implementations of that project) or `Coworkee.Client.Blazor` (proxies for the interfaces it sees) directly. In a project that references both and needs no proxies, set `<CoworkeeServiceProxies>false</CoworkeeServiceProxies>`. Projects referencing the Coworkee sources instead of the packages add the generator themselves:

```xml
<ProjectReference Include="..\coworkee\src\Coworkee.SourceGenerators\Coworkee.SourceGenerators.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

The generated endpoints are ordinary minimal APIs in the OpenAPI document, so the [SDKs](sdk.md) for other languages pick them up as usual. Proxies register when their assembly loads: those of the client app always are; those generated into a class library only when the library is loaded before `AddCoworkeeClient` runs.
