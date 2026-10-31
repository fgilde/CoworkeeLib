# HTTP APIs

Endpoints are minimal APIs that hand a request to the dispatcher. `MapCoworkeeApi` creates the route group, so conventions of the packages (for example [response filters](response-filters.md)) reach every module.

```csharp
var products = app.MapCoworkeeApi("/api/v1/products").WithTags("Products").RequireAuthorization();
products.MapGet("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetProductByIdQuery(id), ct).ToHttpResult());
products.MapPost("/", (AddEditProductRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new AddEditProductCommand(null, body), ct).ToHttpResult());
```

Services with many endpoints can skip the mapping: [application services](application-services.md) are exposed by convention and get a generated Blazor client.

## Results become responses

| Result | Response |
|---|---|
| `Result.Success()` | `204 No Content` |
| `Result<T>` success | `200 OK` with the value |
| `Error.Validation` | `400` problem details with an `errors` dictionary per field |
| `Error.Unauthorized` / `Forbidden` / `NotFound` / `Conflict` | `401` / `403` / `404` / `409` problem details |
| `Error.Unexpected` | `500` problem details |

Every problem details body carries the error's message as `title` and its `code`, so clients can react to `catalog.brand_name_taken` instead of parsing text.

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

Authority and audience come from `Coworkee:ApiAuth`; the [app host](../hosting/aspire.md) sets them. Browsers never hold these tokens: the BFF does, see [Authentication](../security/authentication.md). A single-page app on another origin calls the API with its own token; the API answers it only from the exact origins in `Coworkee:Cors:Origins`, without credentials, and an endpoint that needs them opts in with `.RequireCors(CoworkeeCors.WithCredentials)`, see [cross-origin calls](../security/clients.md#cross-origin-calls-cors).

## OpenAPI

`UseCoworkee` serves the OpenAPI document (`/openapi/v1.json`) and Swagger UI in Development; elsewhere only with `Coworkee:OpenApi:Enabled=true`. Tags and names come from the endpoint definitions (`WithTags`, `WithName`).

The document stays anonymous (SDK generators and snapshot tests read it) and declares a bearer scheme, so the Authorize button works against the API directly. Swagger UI (`/swagger`) needs a signed-in user with `ApiDocs.View`. The BFF forwards `/swagger` and `/openapi`: open `/swagger` on the web app origin (menu entry "API") and it signs you in first, then every request runs as you, with token and CSRF header added for you. A dark-mode toggle follows the system setting.

## Versions

Routes keep the version in the path. A prefix `/api/v{n}/…` in `MapCoworkeeApi` maps the group to version `n` ([Asp.Versioning](https://github.com/dotnet/aspnet-api-versioning) metadata); version 1 is declared by `AddCoworkee`, further versions by the module that brings them:

```csharp
public override void ConfigureServices(ModuleServiceContext context) => context.Services.AddCoworkeeApiVersion(2);

public void ConfigureApplication(WebApplication app)
{
    app.MapCoworkeeApi("/api/v1/products").MapGet("/", ...);
    app.MapCoworkeeApi("/api/v2/products").MapGet("/", ...);
}
```

Each version gets its own document, `/openapi/v2.json`, and an entry in the Swagger UI version picker. `/openapi/v1.json` also keeps every endpoint without a version (plain `MapGroup`, `MapGet`), so it does not change when versioning is in use; v2 lists only v2 endpoints. Paths stay literal, so neither document has a `{version}` parameter.

## Rate limiting

`AddCoworkee` registers the ASP.NET Core rate limiter and `UseCoworkee` runs it after authentication. Limits are fixed windows per signed-in user, or per IP address for anonymous callers; a rejected request gets `429` problem details with code `rate_limited` and a `Retry-After` header.

| Policy | Default | Applied to |
|---|---|---|
| `auth` | 60 per minute | sign-in, registration and password pages of the auth server, `POST /api/v1/identity/me/email`, `PUT /api/v1/identity/me/password` |
| `ai` | 30 per minute | assistant sessions (`/api/v1/ai/sessions`), listing models and testing an AI connection, `/mcp` |
| `apiKeys` | 10 per minute | creating API keys (`POST /api/v1/identity/api-keys`) |
| `upload` | 120 per minute | nothing built in; for your upload endpoints |
| `tokens` | 120 per minute | anonymous token links: temporary file and attachment links, export downloads |
| global | off | every request, once configured |

```csharp
uploads.MapPost("/", ...).RequireCoworkeeRateLimit(CoworkeeRateLimitOptions.Upload);
```

```json title="appsettings.json"
{
  "Coworkee": {
    "RateLimiting": {
      "Enabled": true,
      "Global": { "PermitLimit": 1000, "Window": "00:01:00" },
      "Policies": { "ai": { "PermitLimit": 10 }, "reports": { "PermitLimit": 5, "Window": "00:10:00" } }
    }
  }
}
```

Configuration changes the built-in policies or adds new ones. `Enabled: false` leaves the middleware out. Behind a proxy, forward the client address (`UseForwardedHeaders`), otherwise all anonymous callers share one window. The token endpoint is not limited: behind the BFF all refreshes come from one address.
