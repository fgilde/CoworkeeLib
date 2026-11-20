import { HubConnectionBuilder, HubConnectionState, type HubConnection } from '@microsoft/signalr';

/** What the realtime hub of a Coworkee app sends: an event of a type on a topic (user:{id}, tenant:presence, type:{Entity}, ...). */
export type RealtimeEnvelope<T = unknown> = {
  topic: string;
  type: string;
  payload: T;
  /** Metadata the server fills (older servers leave it out): unique id, when it was sent, who caused it (absent for the system), the request's trace. */
  id?: string;
  at?: string;
  actor?: ActorInfo | null;
  correlationId?: string | null;
};

/** Who caused an event; `impersonatorId` is set while someone is signed in as the user ("Log in as"). */
export type ActorInfo = { userId?: string | null; name?: string | null; impersonatorId?: string | null };

/** A transient notification (`INotifier.ToastAsync`), on the user, session, organisation or global topic. */
export type Toast = {
  title: string;
  body?: string | null;
  severity: 'Normal' | 'Info' | 'Success' | 'Warning' | 'Error';
  link?: string | null;
  /** Set for localizable texts: title and body are English texts with {0} placeholders. */
  arguments?: string[] | null;
  type?: string | null;
};

export type EntityChanged = { entityType: string; entityId: string; action: 'Created' | 'Updated' | 'Deleted'; changedProperties: string[] };

export type RealtimeHandler = (envelope: RealtimeEnvelope) => void;

export type Realtime = {
  /** Follows a topic; the returned function stops it. Topics the server refuses are left out silently. */
  subscribe: (topic: string, handler: RealtimeHandler) => () => void;
  stop: () => Promise<void>;
};

export const realtimeTopics = {
  hub: '/hubs/realtime',
  user: (userId: string) => `user:${userId}`,
  type: (entityType: string) => `type:${entityType}`,
  entity: (entityType: string, id: string) => `entity:${entityType}:${id}`,
  presence: 'tenant:presence',
  tenantPermissions: 'tenant:permissions',
  globalPermissions: 'global:permissions',
  /** Events for the whole organisation (EventTarget.All) and for every organisation (EventTarget.Global). */
  tenantEvents: 'tenant:events',
  globalEvents: 'global:events',
  session: (sessionId: string) => `session:${sessionId}`,
} as const;

export const realtimeEvents = { entityChanged: 'EntityChanged', sessionRevoked: 'SessionRevoked', presenceChanged: 'presence.changed', chatMessage: 'chat.message', chatRead: 'chat.read', toast: 'notification.toast' } as const;

/** The topics an event target of the server (EventTarget) reaches a signed-in user on: his own, the organisation's, the global one and his session's. */
export const targetedTopics = (userId: string, sessionId?: string) =>
  [realtimeTopics.user(userId), realtimeTopics.tenantEvents, realtimeTopics.globalEvents, ...(sessionId ? [realtimeTopics.session(sessionId)] : [])];

/**
 * Events of one type (an `[EventName]` of the server, `EntityChanged`, `notification.toast`) on the given topics, like `Realtime.On<T>` in Blazor:
 * `onEvent<OrderApproved>(realtime, 'orders.approved', targetedTopics(me, sid), (e) => ...)`. `skipActor` leaves out what that user caused himself.
 * The returned function stops every subscription.
 */
export function onEvent<T>(
  realtime: Realtime, type: string, topics: string[], handler: (payload: T, envelope: RealtimeEnvelope<T>) => void, options: { skipActor?: string } = {},
): () => void {
  const receive: RealtimeHandler = (envelope) => {
    if (envelope.type !== type || (options.skipActor && envelope.actor?.userId === options.skipActor)) return;
    handler(envelope.payload as T, envelope as RealtimeEnvelope<T>);
  };
  const stops = topics.map((topic) => realtime.subscribe(topic, receive));
  return () => stops.forEach((stop) => stop());
}

/** Every retry after 0, 2, 10, then 30 seconds, forever - like the Blazor client. */
const delays = [0, 2000, 10000, 30000];

/**
 * The realtime hub of the app (SignalR through the BFF, which adds the token). Connects on the first subscription and subscribes every
 * followed topic again after a reconnect.
 */
export function createRealtime(options: { baseUrl?: string; connection?: HubConnection } = {}): Realtime {
  const handlers = new Map<string, Set<RealtimeHandler>>();
  let connection: HubConnection | undefined = options.connection;
  let started: Promise<boolean> | undefined;
  let wired = false;

  const connect = () => {
    connection ??= new HubConnectionBuilder()
      .withUrl(`${options.baseUrl ?? ''}${realtimeTopics.hub}`, { withCredentials: true })
      .withAutomaticReconnect({ nextRetryDelayInMilliseconds: (context) => delays[Math.min(context.previousRetryCount, delays.length - 1)] })
      .build();
    if (!wired) {
      wired = true;
      connection.on('OnEvent', (envelope: RealtimeEnvelope) => handlers.get(envelope.topic)?.forEach((handler) => handler(envelope)));
      connection.onreconnected(() => { for (const topic of handlers.keys()) void invoke('Subscribe', topic); });
    }
    started = connection.state === HubConnectionState.Disconnected
      ? connection.start().then(() => true, () => { started = undefined; return false; })
      : started ?? Promise.resolve(true);
    return started;
  };

  const invoke = async (method: 'Subscribe' | 'Unsubscribe', topic: string) => {
    try { await connection?.invoke(method, topic); } catch { /* not allowed or not connected: the topic stays quiet */ }
  };

  return {
    subscribe(topic, handler) {
      let set = handlers.get(topic);
      const first = !set;
      if (!set) handlers.set(topic, set = new Set());
      set.add(handler);
      if (first) void connect().then((ok) => ok && handlers.has(topic) ? invoke('Subscribe', topic) : undefined);
      return () => {
        if (!set!.delete(handler) || set!.size > 0) return;
        handlers.delete(topic);
        if (connection?.state === HubConnectionState.Connected) void invoke('Unsubscribe', topic);
      };
    },
    stop: async () => { handlers.clear(); started = undefined; await connection?.stop(); },
  };
}
