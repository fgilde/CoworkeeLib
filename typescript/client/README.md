# @coworkee/client

Typed TypeScript client for the API of apps built on [Coworkee](https://fgilde.github.io/CoworkeeLib/): types generated from the OpenAPI description of the library endpoints, `createCoworkeeClient()` (openapi-fetch), `apiRequest`, `ApiError` with problem details, the realtime hub (`createRealtime()`, SignalR), streamed answers (`postNdjson`), and helpers for the BFF session, theme, texts and OData.

```bash
npm install @coworkee/client
```

```ts
import { createCoworkeeClient, createRealtime, unwrap } from '@coworkee/client';

const api = createCoworkeeClient();               // in a page served by the app's BFF: session cookie + CSRF header
const me = unwrap(await api.GET('/api/v1/identity/me'));
const stop = createRealtime().subscribe(`user:${userId}`, ({ type, payload }) => console.log(type, payload));
```

- Every Coworkee release publishes it in the version of the release (`a.b.c.d` becomes `a.b.(c*1000+d)`, npm knows no fourth part).
- `npm run openapi -- <url of /openapi/v1.json> [more documents]` refreshes `openapi.json`, `npm run generate` the types.
- Documentation: [SDKs](https://fgilde.github.io/CoworkeeLib/web/sdk/)

License: MIT
