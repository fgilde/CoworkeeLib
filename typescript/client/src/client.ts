import createClient, { type Client } from 'openapi-fetch';
import { ApiError, problemOf } from './errors.ts';
import type { paths } from './schema.ts';

export type CoworkeeClient = Client<paths>;

export type CoworkeeClientOptions = {
  /** Address of the app (its BFF) or the API; empty for the page's own origin. */
  baseUrl?: string;
  /** A bearer token of the API (an API key or an access token); without one the browser's session cookie and the CSRF header of the BFF are used. */
  token?: () => string | Promise<string>;
  fetch?: typeof fetch;
};

/** Header the BFF demands on every unsafe request, so other sites cannot send them with the session cookie. */
export const csrfHeader = { name: 'X-CSRF', value: '1' } as const;

async function authorize(headers: Headers, options: CoworkeeClientOptions) {
  if (options.token) headers.set('Authorization', `Bearer ${await options.token()}`);
  else headers.set(csrfHeader.name, csrfHeader.value);
}

/** Typed client over the OpenAPI document of the library: paths, parameters, bodies and answers are checked by the compiler. */
export function createCoworkeeClient(options: CoworkeeClientOptions = {}): CoworkeeClient {
  const client = createClient<paths>({ baseUrl: options.baseUrl ?? '', fetch: options.fetch, credentials: 'same-origin' });
  client.use({
    async onRequest({ request }) {
      await authorize(request.headers, options);
      return request;
    },
  });
  return client;
}

/**
 * Untyped request for any endpoint, the app's own included: JSON in and out, text for other answers, nothing for 204.
 * Bodies that are strings go as JSON, others (Blob, File, FormData) as they are. Failures throw an ApiError.
 */
export async function apiRequest<T = unknown>(path: string, init: RequestInit = {}, options: CoworkeeClientOptions = {}): Promise<T> {
  const headers = new Headers(init.headers);
  await authorize(headers, options);
  if (typeof init.body === 'string' && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json');
  const response = await (options.fetch ?? fetch)(`${options.baseUrl ?? ''}${path}`, { credentials: 'same-origin', ...init, headers });
  if (!response.ok) throw new ApiError(response.status, await problemOf(response));
  if (response.status === 204) return undefined as T;
  return ((response.headers.get('content-type') ?? '').includes('json') ? await response.json() : await response.text()) as T;
}
