# HTTP APIs

Endpoints are minimal APIs that hand a request to the dispatcher. `MapCoworkeeApi` creates the route group, so conventions of the packages (for example [response filters](response-filters.md)) reach every module.

```csharp
var products = app.MapCoworkeeApi("/api/v1/products").WithTags("Products").RequireAuthorization();
products.MapGet("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetProductByIdQuery(id), ct).ToHttpResult());
products.MapPost("/", (AddEditProductRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new AddEditProductCommand(null, body), ct).ToHttpResult());
```

## Results become responses

| Result | Response |
|---|---|
| `Result.Success()` | `204 No Content` |
| `Result<T>` success | `200 OK` with the value |
| `Error.Validation` | `400` problem details with an `errors` dictionary per field |
| `Error.Unauthorized` / `Forbidden` / `NotFound` / `Conflict` | `401` / `403` / `404` / `409` problem details |
| `Error.Unexpected` | `500` problem details |

Every problem details body carries the error `code`, so clients can react to `catalog.brand_name_taken` instead of parsing text.

## Conventions for all API groups

A package or the app can add something to every group created with `MapCoworkeeApi`:

```csharp
services.Configure<CoworkeeApiOptions>(options => options.AddConvention(group => group.AddEndpointFilter<MyFilter>()));
```

## Securing the API

The API validates the bearer tokens of the Coworkee auth server:

```csharp title="MyApp.Api/MyAppApiModule.cs"
context.Services.AddCoworkeeApiAuthentication(context.Configuration, audience: "myapp_api");
```

Authority and audience come from `Coworkee:ApiAuth`; the [app host](../hosting/aspire.md) sets them. Browsers never hold these tokens: the BFF does, see [Authentication](../security/authentication.md).

## OpenAPI

`UseCoworkee` serves the OpenAPI document (`/openapi/v1.json`) and Swagger UI in Development; elsewhere only with `Coworkee:OpenApi:Enabled=true`. Tags and names come from the endpoint definitions (`WithTags`, `WithName`).

The document stays anonymous (SDK generators and snapshot tests read it) and declares a bearer scheme, so the Authorize button works against the API directly. Swagger UI (`/swagger`) needs a signed-in user with `ApiDocs.View`. The BFF forwards `/swagger` and `/openapi`: open `/swagger` on the web app origin (menu entry "API") and it signs you in first, then every request runs as you, with token and CSRF header added for you. A dark-mode toggle follows the system setting.
