# SDKs

Other systems call an app built on Coworkee over its HTTP API. The API describes itself with OpenAPI; from there you write a typed .NET client on `Coworkee.Client` or generate a TypeScript client. The template ships neither a client nor a snapshot of the description: add them when another system needs them.

## The API description

The API serves its OpenAPI description at `/openapi/v1.json` (in Development, or with `Coworkee:OpenApi:Enabled`). The template's API tests read it (`OpenApi_documents_system_info` in `tests/MyApp.Api.Tests/ApiTests.cs`). Once other systems depend on the API, keep a copy of it in the repository, for example `sdk/openapi.json`, and a test that fails when the API changes without the copy:

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

`Pretty` formats the JSON the same way every time (for example `JsonNode.Parse(json)!.ToJsonString(new() { WriteIndented = true })`). A changed snapshot in a diff shows every change of the contract.

## .NET with `Coworkee.Client`

`CoworkeeApiClient` does the plumbing: JSON calls, OData queries and problem details as `CoworkeeApiException` with status, code and validation messages. You add the typed methods of your endpoints:

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

The client depends only on the app's contracts and `Coworkee.Client`, not on Blazor or the server packages.

## TypeScript

For your own endpoints, generate types from the description with `openapi-typescript` and call through `openapi-fetch`; the compiler then checks paths, parameters and bodies. In a folder of its own, such as `sdk/typescript`:

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

The library brings a TypeScript client of its own API: types generated from the OpenAPI description of the library endpoints, a typed `createCoworkeeClient()`, `apiRequest` for any other endpoint, `ApiError` with the problem details, and helpers for the BFF session, the theme, the texts and OData. Inside a page served by the app's BFF it uses the session cookie and the CSRF header; elsewhere give it a token (an [API key](../security/api-keys.md)):

```ts
import { createCoworkeeClient, unwrap, odataPage } from '@coworkee/client';

const api = createCoworkeeClient({ baseUrl: 'https://myapp.example', token: () => apiKey });
const folder = unwrap(await api.GET('/api/v1/files/folders/content', { params: { query: {} } }));
const products = await odataPage('Products', { top: 20, orderBy: 'Name', count: true }, { baseUrl: 'https://myapp.example', token: () => apiKey });
```

`createRealtime()` follows topics of the realtime hub (SignalR through the BFF, e.g. `coworkee.realtime.subscribe('type:Order', ...)`), `postNdjson()` reads streamed answers like those of the assistant.

Install it with `npm install @coworkee/client`; every release publishes it in the version of the release (`a.b.c.d` becomes `a.b.(c*1000+d)`, see [AirWorks client](airworks.md#the-packages)). The [AirWorks client](airworks.md) is built on it. The sources live in `typescript/client`; `npm run openapi -- <url of /openapi/v1.json>` refreshes the description from a running app and `npm run generate` the types.
