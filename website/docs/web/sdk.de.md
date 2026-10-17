# SDKs

Andere Systeme rufen eine App auf Coworkee über ihre HTTP-API auf. Die API beschreibt sich mit OpenAPI; daraus schreibst du einen typisierten .NET-Client auf `Coworkee.Client` oder generierst einen TypeScript-Client. Das Template bringt weder einen Client noch eine Kopie der Beschreibung mit: Leg sie an, wenn ein anderes System sie braucht.

## Die API-Beschreibung

Die API liefert ihre OpenAPI-Beschreibung unter `/openapi/v1.json` (in Development oder mit `Coworkee:OpenApi:Enabled`). Die API-Tests des Templates lesen sie (`OpenApi_documents_system_info` in `tests/MyApp.Api.Tests/ApiTests.cs`). Sobald andere Systeme von der API abhängen, legst du eine Kopie ins Repository, etwa `sdk/openapi.json`, und einen Test, der fehlschlägt, wenn sich die API ohne die Kopie ändert:

```csharp
[Fact]
public async Task OpenApi_matches_the_snapshot()
{
    var live = Pretty(await api.Factory.CreateClient().GetStringAsync("/openapi/v1.json"));
    if (Environment.GetEnvironmentVariable("UPDATE_OPENAPI") == "1")
    {
        await File.WriteAllTextAsync("../../../../../sdk/openapi.json", live);
    }

    (await File.ReadAllTextAsync("../../../../../sdk/openapi.json")).ShouldBe(live, "run with UPDATE_OPENAPI=1 and regenerate the SDKs");
}
```

`Pretty` formatiert das JSON jedes Mal gleich (etwa `JsonNode.Parse(json)!.ToJsonString(new() { WriteIndented = true })`). Eine geänderte Kopie im Diff zeigt jede Änderung des Vertrags.

## .NET mit `Coworkee.Client`

`CoworkeeApiClient` erledigt die Grundlagen: JSON-Aufrufe, OData-Abfragen und Problem Details als `CoworkeeApiException` mit Status, Code und Validierungsmeldungen. Du ergänzt die typisierten Methoden deiner Endpunkte:

```csharp
public sealed class MyAppClient(HttpClient http) : CoworkeeApiClient(http)
{
    public Task<BrandDto> GetBrandAsync(Guid id, CancellationToken cancellationToken = default) =>
        GetAsync<BrandDto>($"api/v1/brands/{id}", cancellationToken);

    public Task<BrandDto> SaveBrandAsync(Guid? id, AddEditBrandRequest brand, CancellationToken cancellationToken = default) =>
        id is null ? SendAsync<BrandDto>(HttpMethod.Post, "api/v1/brands", brand, cancellationToken)
            : SendAsync<BrandDto>(HttpMethod.Put, $"api/v1/brands/{id}", brand, cancellationToken);

    public Task<ODataResult<ProductDto>> GetProductsAsync(string? filter = null, CancellationToken cancellationToken = default) =>
        QueryAsync<ProductDto>("Products", filter, "Name", expand: "Brand", cancellationToken: cancellationToken);
}
```

```csharp
var http = new HttpClient(new BearerTokenHandler(ct => tokens.GetAsync(ct))) { BaseAddress = new Uri("https://myapp.example/") };
var client = new MyAppClient(http);
try
{
    await client.SaveBrandAsync(null, new AddEditBrandRequest());
}
catch (CoworkeeApiException e) when (e.Status == HttpStatusCode.BadRequest)
{
    Console.WriteLine(string.Join(", ", e.Errors["Brand.Name"]));
}
```

Der Client hängt nur von den Contracts der App und `Coworkee.Client` ab, nicht von Blazor oder den Server-Paketen.

## TypeScript

Für deine eigenen Endpunkte generierst du Typen aus der Beschreibung mit `openapi-typescript` und rufst über `openapi-fetch` auf; dann prüft der Compiler Pfade, Parameter und Bodies. In einem eigenen Ordner, etwa `sdk/typescript`:

```bash
npm install openapi-fetch && npm install -D openapi-typescript typescript
npx openapi-typescript ../openapi.json -o src/schema.ts
```

```ts
import createClient from 'openapi-fetch';
import type { paths } from './schema';

const api = createClient<paths>({ baseUrl: 'https://myapp.example', headers: { Authorization: `Bearer ${apiKey}` } });
const { data: brand } = await api.GET('/api/v1/brands/{id}', { params: { path: { id } } });
```

## `@coworkee/client`

Die Bibliothek bringt einen TypeScript-Client ihrer eigenen API mit: aus der OpenAPI-Beschreibung der Bibliotheks-Endpunkte erzeugte Typen, ein typisiertes `createCoworkeeClient()`, `apiRequest` für jeden anderen Endpunkt, `ApiError` mit den Problem Details und Helfer für die BFF-Sitzung, das Theme, die Texte und OData. In einer Seite, die das BFF der App ausliefert, nutzt er das Sitzungs-Cookie und den CSRF-Header; anderswo bekommt er ein Token (einen [API-Schlüssel](../security/api-keys.md)):

```ts
import { createCoworkeeClient, unwrap, odataPage } from '@coworkee/client';

const api = createCoworkeeClient({ baseUrl: 'https://myapp.example', token: () => apiKey });
const folder = unwrap(await api.GET('/api/v1/files/folders/content', { params: { query: {} } }));
const products = await odataPage('Products', { top: 20, orderBy: 'Name', count: true }, { baseUrl: 'https://myapp.example', token: () => apiKey });
```

`createRealtime()` folgt Topics des Realtime-Hubs (SignalR über das BFF, z. B. `coworkee.realtime.subscribe('type:Order', ...)`), `postNdjson()` liest gestreamte Antworten wie die des Assistenten.

Installation mit `npm install @coworkee/client`; jedes Release veröffentlicht es in der Version des Releases (`a.b.c.d` wird zu `a.b.(c*1000+d)`, siehe [AirWorks-Client](airworks.md#die-pakete)). Der [AirWorks-Client](airworks.md) baut darauf auf. Die Quellen liegen in `typescript/client`; `npm run openapi -- <Adresse von /openapi/v1.json>` aktualisiert die Beschreibung aus einer laufenden App und `npm run generate` die Typen.
