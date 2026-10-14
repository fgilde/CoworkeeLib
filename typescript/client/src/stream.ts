import { csrfHeader, type CoworkeeClientOptions } from './client.ts';
import { ApiError, problemOf } from './errors.ts';

/** Splits text that arrives in chunks into complete lines; the rest waits for the next chunk. */
export function lineSplitter() {
  let rest = '';
  return (chunk: string, end = false): string[] => {
    const lines = (rest + chunk).split('\n');
    rest = end ? '' : lines.pop() ?? '';
    return lines.map((line) => line.trim()).filter(Boolean);
  };
}

/** POSTs JSON and hands every line of the newline-delimited JSON answer (application/x-ndjson) to onEvent as it arrives. */
export async function postNdjson<T>(path: string, body: unknown, onEvent: (event: T) => void, options: CoworkeeClientOptions & { signal?: AbortSignal } = {}): Promise<void> {
  const headers = new Headers({ 'Content-Type': 'application/json', Accept: 'application/x-ndjson' });
  if (options.token) headers.set('Authorization', `Bearer ${await options.token()}`);
  else headers.set(csrfHeader.name, csrfHeader.value);
  const response = await (options.fetch ?? fetch)(`${options.baseUrl ?? ''}${path}`, { method: 'POST', credentials: 'same-origin', headers, body: JSON.stringify(body), signal: options.signal });
  if (!response.ok) throw new ApiError(response.status, await problemOf(response));
  const split = lineSplitter();
  const decoder = new TextDecoder();
  const reader = response.body?.getReader();
  if (!reader) return split(await response.text(), true).forEach((line) => onEvent(JSON.parse(line) as T));
  for (;;) {
    const { done, value } = await reader.read();
    split(done ? decoder.decode() : decoder.decode(value, { stream: true }), done).forEach((line) => onEvent(JSON.parse(line) as T));
    if (done) return;
  }
}
