# HTTP-APIs

Endpunkte sind Minimal-APIs, die einen Request an den Dispatcher geben. `MapCoworkeeApi` legt die Routengruppe an, damit Konventionen der Pakete (etwa [Response-Filter](response-filters.md)) jedes Modul erreichen.

```csharp
var products = app.MapCoworkeeApi("/api/v1/products").WithTags("Products").RequireAuthorization();
products.MapGet("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetProductByIdQuery(id), ct).ToHttpResult());
products.MapPost("/", (AddEditProductRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new AddEditProductCommand(null, body), ct).ToHttpResult());
```

## Aus Results werden Antworten

| Result | Antwort |
|---|---|
| `Result.Success()` | `204 No Content` |
| `Result<T>` erfolgreich | `200 OK` mit dem Wert |
| `Error.Validation` | `400` Problem Details mit einem `errors`-Wörterbuch pro Feld |
| `Error.Unauthorized` / `Forbidden` / `NotFound` / `Conflict` | `401` / `403` / `404` / `409` Problem Details |
| `Error.Unexpected` | `500` Problem Details |

Jede Problem-Details-Antwort enthält den `code` des Fehlers, damit Clients auf `catalog.brand_name_taken` reagieren können, statt Text auszuwerten.

## Konventionen für alle API-Gruppen

Ein Paket oder die App kann jeder mit `MapCoworkeeApi` angelegten Gruppe etwas hinzufügen:

```csharp
services.Configure<CoworkeeApiOptions>(options => options.AddConvention(group => group.AddEndpointFilter<MyFilter>()));
```

## Die API absichern

Die API prüft die Bearer-Tokens des Coworkee-Auth-Servers:

```csharp title="MyApp.Api/MyAppApiModule.cs"
context.Services.AddCoworkeeApiAuthentication(context.Configuration, audience: "myapp_api");
```

Authority und Audience kommen aus `Coworkee:ApiAuth`; der [AppHost](../hosting/aspire.md) setzt sie. Browser halten diese Tokens nie: Das übernimmt der BFF, siehe [Anmeldung](../security/authentication.md).

## OpenAPI

`UseCoworkee` liefert in der Umgebung Development das OpenAPI-Dokument (`/openapi/v1.json`) und die Swagger-Oberfläche aus, sonst nur mit `Coworkee:OpenApi:Enabled=true`. Tags und Namen kommen aus den Endpunktdefinitionen (`WithTags`, `WithName`).
