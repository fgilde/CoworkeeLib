# SDKs

Eine App auf Coworkee liefert zwei Clients für andere Systeme: einen typisierten .NET-Client und einen aus der API-Beschreibung generierten TypeScript-Client.

## Die API-Beschreibung

Die API liefert ihre OpenAPI-Beschreibung unter `/openapi/v1.json` (in Development oder mit `Coworkee:OpenApi:Enabled`). Das Template hält eine Kopie in `sdk/openapi.json`, und ein Test schlägt fehl, wenn sich die API ohne die Kopie ändert:

```csharp
var live = await api.Factory.CreateClient().GetStringAsync("/openapi/v1.json");
(await File.ReadAllTextAsync("sdk/openapi.json")).ShouldBe(Pretty(live), "run with MYAPP_UPDATE_OPENAPI=1 and regenerate the SDKs");
```

## .NET mit `Coworkee.Client`

`CoworkeeApiClient` erledigt die Grundlagen: JSON-Aufrufe, OData-Abfragen und Problem Details als `CoworkeeApiException` mit Status, Code und Validierungsmeldungen. Die App ergänzt typisierte Methoden:

```csharp
public sealed class MyAppClient(HttpClient http) : CoworkeeApiClient(http)
{
    public Task<BrandDto> GetBrandAsync(Guid id, CancellationToken cancellationToken = default) =>
        GetAsync<BrandDto>($"api/v1/brands/{id}", cancellationToken);

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

`sdk/typescript` generiert Typen mit `openapi-typescript` und ruft über `openapi-fetch` auf; Pfade, Parameter und Bodies prüft der Compiler:

```bash
cd sdk/typescript && npm install && npm run generate && npm run build
```

```ts
const api = createMyAppClient("https://myapp.example", () => getToken());
const { data: brand } = await api.GET("/api/v1/brands/{id}", { params: { path: { id } } });
```
