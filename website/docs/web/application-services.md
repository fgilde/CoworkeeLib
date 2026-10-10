# Application services

An application service is an interface in the contracts. The server exposes its implementation as HTTP endpoints by convention, and the Blazor client gets an implementation of the same interface that calls them. Neither side writes HTTP code: a source generator, shipped in `Coworkee.AspNetCore` and `Coworkee.Client.Blazor`, writes the endpoints and the client proxies at build time (no reflection at runtime, so trimming and AOT in WebAssembly are fine).

**Why**: one interface is the whole API contract. The page calls `Orders.CancelAsync(id)` like a local service, the server gets an ordinary minimal API with permissions, validation and the unit of work, and the OpenAPI document and SDKs see the same endpoints. **Compared with** [hand-written endpoints](http-api.md) and the [dispatcher](../fundamentals/messaging.md): use a service for the operations a client calls; keep commands for work that also runs from jobs, imports or the AI assistant (a service method can simply send the command).

The template has two: `IDocumentTypeAppService` (Documents) and `ISalesOrderAppService` (Sales). This page builds the second one step by step.

## Step 1: the interface in the contracts

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

`IApplicationService` (package `Coworkee.Contracts`) marks it. Only methods returning `Task`, `Task<T>`, `ValueTask` or `ValueTask<T>` are allowed; the build tells you otherwise.

## Step 2: the implementation in the module

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

There is no registration and no mapping: `AddCoworkee` registers every implementation found in the assemblies of the modules (scoped, unless the interface is registered already) and `UseCoworkee` maps it. The implementation changes the `DbContext` and does not save; the endpoint does (step 4). The `DocumentTypeAppService` of the template shows the other style: each method sends a command through the dispatcher and turns a failed result into an exception with `OrThrow()`.

Hosts without modules call `services.AddCoworkeeApplicationServices(assembly)` and `app.MapCoworkeeApplicationServices()`.

## Step 3: what is generated

The service name is `SalesOrder` (interface name without `I` and `AppService`), so the route is `/api/v1/sales-orders` and the OpenAPI tag "Sales orders":

| Method | Endpoint | Answer |
|---|---|---|
| `GetAsync(Guid id)` | `GET /api/v1/sales-orders/{id}` | `200` with the order, `404` |
| `CreateAsync(AddEditSalesOrderRequest input)` | `POST /api/v1/sales-orders` with the request as body | `200` with the order, `400` |
| `UpdateAsync(Guid id, AddEditSalesOrderRequest input)` | `PUT /api/v1/sales-orders/{id}` | `200`, `400`, `404` |
| `CancelAsync(Guid id)` | `POST /api/v1/sales-orders/{id}/cancel` | `204` |
| `DeleteAsync(IdsRequest request)` | `POST /api/v1/sales-orders/delete` (from `[ServiceOperation]`) | `204` |

On the server the generator writes one minimal API per method into `CoworkeeApplicationServiceEndpoints.g.cs`, for example:

```csharp
endpoints.MapMethods("{id:guid}/cancel", ["POST"],
    static async ([FromRoute(Name = "id")] Guid id, CancellationToken cancellationToken, [FromServices] ISalesOrderAppService service) =>
    {
        await service.CancelAsync(id, cancellationToken);
        return TypedResults.NoContent();
    });
```

In the client it writes `CoworkeeServiceProxies.g.cs`, a class per interface:

```csharp
internal sealed class SalesOrderAppServiceProxy(HttpClient http) : ApiClientBase(http), ISalesOrderAppService
{
    Task ISalesOrderAppService.CancelAsync(Guid id, CancellationToken cancellationToken)
        => SendAsync(HttpMethod.Post, "api/v1/sales-orders/" + ServiceProxyUrl.Segment(id) + "/cancel", null, cancellationToken);
    // …
}
```

To look at the files, open *Dependencies → Analyzers → Coworkee.SourceGenerators* in the IDE, or set `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>` and find them under `obj/…/generated/Coworkee.SourceGenerators/`. The endpoints are in the OpenAPI document like any other (`/openapi/v1.json`), so the [SDKs](sdk.md) for TypeScript and other languages get them too.

## Step 4: permissions, validation, errors, saving

Every application service endpoint requires a signed-in user and runs, in this order:

1. `[RequiresPermission]` on the implementing class or method: `401` without a user, `403` without the permission.
2. FluentValidation: every validator registered for an argument type (`IValidator<AddEditSalesOrderRequest>`, registered by `AddMessagingFromAssembly`) runs; failures come back as `400` with `errors` per field.
3. The method.
4. The unit of work (`IUnitOfWork.SaveChangesAsync`) for everything but GET, so implementations that change the `DbContext` directly need no save.

Errors are exceptions: `ErrorException` carries an `Error` and becomes the same problem details as a failed `Result` (see [HTTP APIs](http-api.md)); `SalesErrors.OrderNotFound` is a `404`, `SalesErrors.UnknownCustomer` a `400` on the field `CustomerId`.

## Step 5: the client

`AddCoworkeeClient` registers a proxy for every application service the client project sees, as typed `HttpClient` with the base address of the app. Inject the interface:

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

Proxies derive from `ApiClientBase`: they go through the BFF with the CSRF header and throw `ApiException` with the problem details, which `Snackbar.RunAsync` and the edit dialog show. The table of the page reads through OData; the orders are a [client entity](../ui/client-entities.md), whose local copy refreshes right after such a write.

In tests, substitute the interface: `Services.AddSingleton(Substitute.For<ISalesOrderAppService>())`.

## Conventions

The service name is the interface name without `I` and without `AppService`, `ApplicationService` or `Service`. The route is `/api/v1/` plus the plural in kebab case, the OpenAPI tag the plural in words.

The method name (without `Async`) picks the HTTP method:

| Name starts with | Method | Route part from the name |
|---|---|---|
| `Get` | GET | rest of the name; none for `Get`, `GetList`, `GetAll`, `GetById` |
| `Find`, `List`, `Search`, `Query`, `Count` | GET | the whole name, for example `search` |
| `Create`, `Add`, `Insert` | POST | rest of the name |
| `Update` | PUT | rest of the name |
| `Patch` | PATCH | rest of the name |
| `Delete`, `Remove` | DELETE | rest of the name |
| anything else | POST | the whole name, for example `cancel` |

Parameters bind like this:

| Parameter | Comes from |
|---|---|
| `CancellationToken` | the request |
| named in the route, or `id` of a simple type | the route, `{id:guid}` for a Guid (`:int`, `:long` likewise) |
| simple type (numbers, string, bool, Guid, dates, enums) or a list of them | the query string |
| other types with POST, PUT, PATCH | the JSON body (only one per method) |
| other types with GET, DELETE | the query string, one value per property (`[AsParameters]`) |

Methods return `Task`, `Task<T>`, `ValueTask` or `ValueTask<T>`: a value comes back as `200` with JSON, no value as `204`.

## Overriding the conventions

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

`Route` of `[ServiceOperation]` is relative to the service; `{name}` placeholders bind the parameters of the same name. An ignored method gets no endpoint and its proxy throws `NotSupportedException`, so the interface can still hold methods for calls in process.

The build fails with a clear message when a contract cannot work over HTTP: `CWK001` for members that are not methods returning a task (or generic, or with `ref`/`out`), `CWK002` for two body parameters or a route placeholder without parameter, `CWK003` for two methods with the same method and route.

## Configuring single services

Defaults for all services first, then the settings of single ones.

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
| `Route`, `Tag` | from the conventions and `[ApplicationService]` |
| `Enabled` | `false` keeps the service off the API; it stays registered for calls in process |
| `RequireAuthorization` | `true` by default |
| `Conventions` | applied to the endpoint group of the service |
| `Properties` | settings other features attach to a service, such as `ClientEntity<TEntity>()` of `Coworkee.OData` ([client entities](../ui/client-entities.md)) |

A feature of your own attaches settings the same way, with an extension method that writes to `Properties`, and reads them back with `serviceProvider.GetApplicationServices()`, which lists every registered service with its final settings.

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

Proxies register when their assembly loads: those of the client app always are; those generated into a class library only when the library is loaded before `AddCoworkeeClient` runs.

## Files of the sample

| File | |
|---|---|
| `MyApp.Contracts/Sales/ISalesOrderAppService.cs` | the interface |
| `MyApp.Contracts/Sales/AddEditSalesOrderRequest.cs`, `SalesOrderDto.cs` | request and answer |
| `MyApp.Sales/Features/Orders/SalesOrderAppService.cs` | the implementation |
| `MyApp.Sales/Features/Orders/AddEditSalesOrderValidator.cs` | validation of the request |
| `MyApp.Web.Client/Pages/Sales/SalesOrders.razor.cs` | the page using the generated client |
| `tests/MyApp.Api.Tests/SalesTests.cs` | the endpoints over HTTP |
