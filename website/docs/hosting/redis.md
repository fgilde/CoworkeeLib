# Scaling out with Redis

Redis is optional. A service that has a `redis` connection string shares its state with the other instances through it; without one, everything stays in the process and works the same for a single instance.

| What | With Redis | Without |
|---|---|---|
| SignalR | backplane: an event reaches the connections of every instance | the connections of this instance |
| Presence | a user counts once, whichever instances hold connections | per instance |
| Permission, setting, feature and text caches | a change drops the entries on every instance at once | other instances notice within the cache lifetime |
| Session stamps and session changes | shared stamp cache, pub/sub to every instance ([Clients and sessions](../security/clients.md)) | own instance, the others within a minute |
| Topics after permission changes | every instance checks its connections again ([Realtime](../modules/realtime.md)) | own instance |
| Sign-in sessions of the web app | session store and the key ring of the cookie in Redis: shared by the instances, they survive restarts | memory and the local key ring |
| Distributed lock | `Coworkee:DistributedLock:Provider = Redis` ([Events and locks](../fundamentals/events.md)) | the database: a Postgres advisory lock, a SQL Server application lock, a file lock with SQLite |

## Aspire

The app host gives every service that references `Coworkee.Realtime` (directly or through `Coworkee.Identity`) a Redis container, and the web app as well. Use your own Redis, or none:

```csharp
builder.AddCoworkeeApp("shop", options =>
{
    // production: an existing Redis
    options.UseRedis(builder.AddConnectionString("redis"));

    // or: one instance per service, no container
    options.WithoutRedis();
});
```

`coworkee new --redis false` generates the app host with `WithoutRedis()`. With Redis (the default), the generated service defaults also call `AddRedisClient("redis")` from `Aspire.StackExchange.Redis`: a health check and traces for the connection, which the Coworkee services reuse.

## How it works

- **Caches.** HybridCache keeps the tagged caches (permissions, settings, features, texts, cached queries) in each instance, because a removal by tag does not reach a second level other instances read. Every removal by key or tag also goes out on the channel `coworkee:cache-invalidation`; the other instances drop the same entries from their local cache.
- **Presence.** Each instance writes the users it holds connections for into its own hash `coworkee:presence:<instance>`, which expires after 30 seconds unless the instance writes it again (every 10 seconds). A user who connects is announced only when no other instance has them online; a user who leaves is announced offline only when no other instance still holds a connection. The users of a crashed instance drop out with its hash.
- **Web app.** The data protection keys of the session cookie are kept under `coworkee:bff:data-protection`, so any instance reads a cookie another one wrote.

A Redis that does not answer is not fatal: the services log a warning and fall back to what they know locally.

!!! note "Limits"
    The presence query reads the hash of every instance; that is fine for a handful of instances. Users of a crashed instance turn offline in the presence list, but no offline event is sent for them.
