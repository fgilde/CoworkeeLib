# @coworkee/client

Typed TypeScript client for the API of apps built on [Coworkee](https://fgilde.github.io/CoworkeeLib/): types generated from the OpenAPI description of the library endpoints, `createCoworkeeClient()` (openapi-fetch), `apiRequest`, `ApiError` with problem details, and helpers for the BFF session, theme, texts and OData.

```ts
import { createCoworkeeClient, unwrap } from '@coworkee/client';

const api = createCoworkeeClient();               // in a page served by the app's BFF: session cookie + CSRF header
const me = unwrap(await api.GET('/api/v1/identity/me'));
```

- `npm run openapi -- <url of /openapi/v1.json> [more documents]` refreshes `openapi.json`, `npm run generate` the types.
- Documentation: [SDKs](https://fgilde.github.io/CoworkeeLib/web/sdk/)

License: MIT