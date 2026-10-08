# SDKs

An app built on Coworkee ships two clients for other systems: a typed .NET client and a TypeScript client generated from the API description.

## The API description

The API serves its OpenAPI description at `/openapi/v1.json` (in Development, or with `Coworkee:OpenApi:Enabled`). The template keeps a copy in `sdk/openapi.json`, and a test fails when the API changes without the copy:

```csharp
var live = await api.Factory.CreateClient().GetStringAsync("/openapi/v1.json");
(await File.ReadAllTextAsync("sdk/openapi.json")).ShouldBe(Pretty(live), "run with MYAPP_UPDATE_OPENAPI=1 and regenerate the SDKs");
```

## .NET with `Coworkee.Client`

`CoworkeeApiClient` does the plumbing: JSON calls, OData queries and problem details as `CoworkeeApiException` with status, code and validation messages. The app adds typed methods:

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

The client depends only on the app's contracts and `Coworkee.Client`, not on Blazor or the server packages.

## TypeScript

`sdk/typescript` generates types with `openapi-typescript` and calls through `openapi-fetch`, so paths, parameters and bodies are checked by the compiler:

```bash
cd sdk/typescript && npm install && npm run generate && npm run build
```

```ts
const api = createMyAppClient("https://myapp.example", () => getToken());
const { data: brand } = await api.GET("/api/v1/brands/{id}", { params: { path: { id } } });
```
