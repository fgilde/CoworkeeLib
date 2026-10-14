import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ApiError, apiRequest, createCoworkeeClient, problemMessage, unwrap } from '../src/index.ts';

type Call = { url: string; headers: Headers; method: string; body?: unknown };

function fakeFetch(answer: Response, calls: Call[]): typeof fetch {
  return (async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(String(input), init);
    calls.push({ url: request.url, headers: request.headers, method: request.method, body: init?.body });
    return answer.clone();
  }) as typeof fetch;
}

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });

test('requests through the BFF carry the CSRF header, with a token the bearer instead', async () => {
  const calls: Call[] = [];
  await apiRequest('/api/v1/identity/me', { method: 'PUT', body: '{}' }, { baseUrl: 'https://app.test', fetch: fakeFetch(json({}), calls) });
  await apiRequest('/api/v1/identity/me', {}, { baseUrl: 'https://app.test', token: () => 'key', fetch: fakeFetch(json({}), calls) });

  assert.equal(calls[0].headers.get('X-CSRF'), '1');
  assert.equal(calls[0].headers.get('Content-Type'), 'application/json');
  assert.equal(calls[1].headers.get('Authorization'), 'Bearer key');
  assert.equal(calls[1].headers.get('X-CSRF'), null);
});

test('failures become ApiError with the validation messages', async () => {
  const problem = { title: 'One or more validation errors occurred.', code: 'validation', errors: { Name: ['Name must not be empty.'] } };

  await assert.rejects(apiRequest('/api/v1/files/folders', { method: 'POST', body: '{}' }, { baseUrl: 'https://app.test', fetch: fakeFetch(json(problem, 400), []) }),
    (error: unknown) => error instanceof ApiError && error.status === 400 && error.code === 'validation' && error.message === 'Name must not be empty.');
  assert.equal(problemMessage(404), 'The API answered 404.');
  assert.equal(problemMessage(409, { title: 'Conflict', detail: 'The name is taken.' }), 'The name is taken.');
});

test('204 gives nothing, text stays text', async () => {
  assert.equal(await apiRequest('/x', {}, { baseUrl: 'https://app.test', fetch: fakeFetch(new Response(null, { status: 204 }), []) }), undefined);
  assert.equal(await apiRequest('/x', {}, { baseUrl: 'https://app.test', fetch: fakeFetch(new Response('hi', { headers: { 'content-type': 'text/plain' } }), []) }), 'hi');
});

test('the typed client sends the CSRF header and unwrap returns the data', async () => {
  const calls: Call[] = [];
  const api = createCoworkeeClient({ baseUrl: 'https://app.test', fetch: fakeFetch(json({ folders: [], files: [], canUpload: true, canManage: false }), calls) });

  const content = unwrap(await api.GET('/api/v1/files/folders/content', { params: { query: { folderId: undefined } } }));

  assert.equal(content.canUpload, true);
  assert.equal(calls[0].url, 'https://app.test/api/v1/files/folders/content');
  assert.equal(calls[0].headers.get('X-CSRF'), '1');
});
