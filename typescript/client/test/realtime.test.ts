import assert from 'node:assert/strict';
import { test } from 'node:test';
import { HubConnectionState } from '@microsoft/signalr';
import { createRealtime, lineSplitter, postNdjson, type RealtimeEnvelope } from '../src/index.ts';

function fakeConnection() {
  const calls: string[] = [];
  const events: Record<string, (envelope: RealtimeEnvelope) => void> = {};
  let reconnected = () => {};
  const connection = {
    state: HubConnectionState.Disconnected as HubConnectionState,
    on: (name: string, handler: (envelope: RealtimeEnvelope) => void) => { events[name] = handler; },
    onreconnected: (handler: () => void) => { reconnected = handler; },
    start: async () => { calls.push('start'); connection.state = HubConnectionState.Connected; },
    stop: async () => { connection.state = HubConnectionState.Disconnected; },
    invoke: async (method: string, topic: string) => { calls.push(`${method} ${topic}`); },
  };
  return { connection, calls, send: (envelope: RealtimeEnvelope) => events.OnEvent(envelope), reconnect: () => reconnected() };
}

const tick = () => new Promise((resolve) => setTimeout(resolve, 0));

test('the hub starts once, each topic is subscribed once and events reach the handlers of their topic', async () => {
  const fake = fakeConnection();
  const realtime = createRealtime({ connection: fake.connection as never });
  const seen: string[] = [];
  const stopA = realtime.subscribe('user:1', (envelope) => seen.push(`a ${envelope.type}`));
  realtime.subscribe('user:1', (envelope) => seen.push(`b ${envelope.type}`));
  realtime.subscribe('tenant:presence', () => seen.push('presence'));
  await tick();
  fake.send({ topic: 'user:1', type: 'chat.message', payload: {} });
  stopA();
  fake.send({ topic: 'user:1', type: 'chat.read', payload: {} });

  assert.deepEqual(fake.calls, ['start', 'Subscribe user:1', 'Subscribe tenant:presence']);
  assert.deepEqual(seen, ['a chat.message', 'b chat.message', 'b chat.read']);
});

test('the last handler of a topic unsubscribes it, a reconnect subscribes the followed topics again', async () => {
  const fake = fakeConnection();
  const realtime = createRealtime({ connection: fake.connection as never });
  const stop = realtime.subscribe('type:Product', () => {});
  realtime.subscribe('user:1', () => {});
  await tick();
  stop();
  fake.reconnect();
  await tick();

  assert.deepEqual(fake.calls, ['start', 'Subscribe type:Product', 'Subscribe user:1', 'Unsubscribe type:Product', 'Subscribe user:1']);
});

test('lines of a stream come out whole, however the chunks are cut', () => {
  const split = lineSplitter();
  assert.deepEqual(split('{"a":1}\n{"b"'), ['{"a":1}']);
  assert.deepEqual(split(':2}\n\n'), ['{"b":2}']);
  assert.deepEqual(split('{"c":3}', true), ['{"c":3}']);
});

test('postNdjson sends JSON with the CSRF header and hands over each event', async () => {
  const events: unknown[] = [];
  let sent: RequestInit | undefined;
  const fetch = (async (_: string, init: RequestInit) => {
    sent = init;
    return new Response('{"type":"delta","text":"Hi"}\n{"type":"done"}\n', { headers: { 'content-type': 'application/x-ndjson' } });
  }) as typeof globalThis.fetch;

  await postNdjson('/api/v1/ai/sessions/1/messages', { text: 'Hello' }, (event) => events.push(event), { fetch });

  assert.equal(new Headers(sent!.headers).get('X-CSRF'), '1');
  assert.equal(sent!.body, '{"text":"Hello"}');
  assert.deepEqual(events, [{ type: 'delta', text: 'Hi' }, { type: 'done' }]);
});
