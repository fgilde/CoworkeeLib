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

Das Dokument bleibt anonym lesbar (SDK-Generatoren und Snapshot-Tests lesen es) und deklariert ein Bearer-Schema, sodass der Authorize-Knopf direkt gegen die API funktioniert. Die Swagger-Oberfläche (`/swagger`) braucht einen angemeldeten Benutzer mit `ApiDocs.View`. Das BFF leitet `/swagger` und `/openapi` weiter: `/swagger` auf der Adresse der Web-App öffnen (Menüeintrag „API“) meldet zuerst an, danach läuft jede Anfrage als Sie, Token und CSRF-Header kommen automatisch dazu. Ein Dunkelmodus-Schalter folgt der Systemeinstellung.

## Versionen

Routen tragen die Version im Pfad. Ein Präfix `/api/v{n}/…` in `MapCoworkeeApi` ordnet die Gruppe der Version `n` zu ([Asp.Versioning](https://github.com/dotnet/aspnet-api-versioning)-Metadaten); Version 1 deklariert `AddCoworkee`, weitere Versionen das Modul, das sie mitbringt:

```csharp
public override void ConfigureServices(ModuleServiceContext context) => context.Services.AddCoworkeeApiVersion(2);

public void ConfigureApplication(WebApplication app)
{
    app.MapCoworkeeApi("/api/v1/products").MapGet("/", ...);
    app.MapCoworkeeApi("/api/v2/products").MapGet("/", ...);
}
```

Jede Version bekommt ein eigenes Dokument, `/openapi/v2.json`, und einen Eintrag in der Versionsauswahl der Swagger-Oberfläche. `/openapi/v1.json` enthält weiterhin alle Endpunkte ohne Version (einfaches `MapGroup`, `MapGet`) und ändert sich durch die Versionierung nicht; v2 listet nur v2-Endpunkte. Die Pfade bleiben wörtlich, kein Dokument hat einen Parameter `{version}`.

## Rate Limiting

`AddCoworkee` registriert den Rate Limiter von ASP.NET Core, `UseCoworkee` führt ihn nach der Authentifizierung aus. Die Grenzen sind feste Zeitfenster pro angemeldetem Benutzer, für anonyme Aufrufer pro IP-Adresse; eine abgewiesene Anfrage bekommt `429` als Problem Details mit dem Code `rate_limited` und einem `Retry-After`-Header.

| Policy | Standard | Gilt für |
|---|---|---|
| `auth` | 60 pro Minute | Anmelde-, Registrierungs- und Passwortseiten des Auth-Servers |
| `ai` | 30 pro Minute | `/api/v1/ai/*` und `/mcp` |
| `upload` | 120 pro Minute | nichts Eingebautes; für eigene Upload-Endpunkte |
| global | aus | jede Anfrage, sobald konfiguriert |

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

Die Konfiguration ändert die eingebauten Policies oder fügt neue hinzu. `Enabled: false` lässt die Middleware weg. Hinter einem Proxy die Client-Adresse weiterreichen (`UseForwardedHeaders`), sonst teilen sich alle anonymen Aufrufer ein Zeitfenster. Der Token-Endpunkt ist nicht begrenzt: Hinter dem BFF kommen alle Refreshes von einer Adresse.
