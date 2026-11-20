# Spec: unified events, targets and notifications (Coworkee lib)

Status: approved by the owner 2026-11-20 ("die Erweiterungen gefallen mir"; design decisions delegated). Build test-driven: write the failing tests of a phase first, then the code.

## Goal

One simple, abstract model for everything that "happens" in an app, usable the same way in the backend and in Blazor, without losing any current behaviour:

- **one source of entity changes** (today four copies of the change-tracker scan: `RealtimeChangeInterceptor`, `WebhookChangeInterceptor`, `Coworkee.Workflows.Elsa.EntityChangeInterceptor`, `AuditTrailInterceptor`),
- **one target model** (`EventTarget`) shared by realtime, toasts, stored notifications and mail,
- **one notification facade** that can toast, store, mail (never / always / when offline / by preference),
- **who / when / why** on every event.

Non-goals: replacing the outbox, inbox, RabbitMQ transport, notification types/preferences/following, realtime topic authorizers, audit queries/restore. They stay and get fed by the new pieces.

## Principles

- Extend, never break: every current public API keeps working (`IRealtimePublisher`, `[Realtime]`, `RealtimeTopics`, `INotifier.NotifyAsync`, `IDomainEventHandler<T>`, webhooks, Elsa `Watch<T>`, audit). Old records may become thin adapters over the new ones.
- Security first: subscribe-time authorization stays; a target only narrows. No entity data is sent to a target that was not checked for the data (default payload = ids + changed property names + actor + time, as today plus metadata).
- Transaction-correct: everything leaves after commit; rollback drops it.
- Tenant-isolated: targets resolve inside the current tenant unless explicitly global.
- Nothing gets slower: no extra DB round trip per SaveChanges for apps that don't opt in; one change-tracker scan instead of four.
- Stable event names: an `[EventName("...")]` attribute or a registry, never the CLR full name.

## 1. EventTarget (who receives)

```csharp
public abstract record EventTarget
{
    public static EventTarget All { get; }                 // every user of the tenant
    public static EventTarget Global { get; }               // every user of every tenant (system only)
    public static EventTarget Current { get; }              // the current session
    public static EventTarget Users(params Guid[] ids);
    public static EventTarget Users(Expression<Func<EventUser, bool>> predicate);
    public static EventTarget Permission(string permission) => Users(u => u.HasPermission(permission));
    public static EventTarget Role(string role)            => Users(u => u.IsInRole(role));
    public static EventTarget Group(Guid organisationUnitId) => Users(u => u.InGroup(organisationUnitId));
    public static EventTarget Custom<TResolver>(object? args = null) where TResolver : IAudienceResolver;
    public EventTarget Except(EventTarget other);           // e.g. .Except(EventTarget.Current) instead of SkipCurrentUser
    public static EventTarget operator |(EventTarget a, EventTarget b);
}
```

- `EventUser` is a query view over the lib's user (id, user name, email, display name, tenant, active, profile/extended attributes the app maps) plus marker methods `HasPermission(string)`, `IsInRole(string)`, `InGroup(Guid)` (including ancestors), `IsOnline()`.
- The marker methods are translated to SQL subqueries over the lib's own role/permission/organisation-unit tables (an expression visitor), so the whole predicate runs in the database. The permission semantics must equal `IPermissionChecker` (roles, direct grants, organisation units, tenant features). Prove that with tests that compare the resolver against the checker for a seeded matrix. If a part is not translatable, fail fast with a clear message (no silent client evaluation of all users).
- Resolution (`IEventTargetResolver`):
  - for realtime/toasts: `All` → the tenant group (no resolution); `Current` → session group; everything else → **online users of the tenant first** (`RealtimePresence`), then the predicate for those ids only → send to their `user:{id}` groups. Cheap even for big tenants.
  - for stored notifications/mail: resolve in the database to user ids (batched), active users only.
- `IAudienceResolver` (app hook): CoShipee's `HubAudience` (groups mapped to a shipment's parties + permission) becomes a resolver, usable as `EventTarget.Custom<ShipmentAudience>(shipmentId)`.

## 2. Envelope metadata (who / when)

`RealtimeEnvelope` gains optional `Id` (Guid), `At` (DateTimeOffset), `Actor` (`ActorInfo(Guid? UserId, string? Name, Guid? ImpersonatorId)`), `CorrelationId`. Old clients ignore unknown fields; old publishers keep compiling (the publisher fills metadata from `ICurrentUser`/clock). Client components can ignore own changes (`c.Actor.UserId == me`) and show "X changed this".

## 3. One source of entity changes

- A single `EntityChangeCollector` (SaveChanges + transaction interceptor) scans the change tracker once per save and produces

```csharp
public sealed record EntityChange(string EntityType, string EntityId, ChangeAction Action /*Created|Updated|Deleted|Restored*/,
    IReadOnlyList<PropertyChange> Changes /*Property, Old, New; masked like audit; values only for sinks that need them*/,
    ActorInfo Actor, DateTimeOffset At, string? Reason, Guid? CorrelationId, Guid? TenantId);
public sealed record EntityChange<TEntity>(...) : typed view (EntityId typed, same data)
```

- Sinks (`IEntityChangeSink`, ordered, run at the right moment: in-save for audit and outbox rows, after-commit for realtime): Audit, Realtime, Webhooks, Elsa, and the new durable handlers. Each existing interceptor becomes a sink; behaviour and records stay byte-compatible (`EntityChangedPayload`, `WebhookEntityChanged`, `Elsa.EntityChanged`, `AuditEntry`). One soft-delete rule, one action mapping.
- Reason: `using (ChangeReason.Begin("Import from CW1")) { ... }` (AsyncLocal scope) and `IHasChangeReason { string? Reason }` on a request (the unit-of-work middleware opens the scope). Stored on `AuditEntry` (new nullable column `Reason` → lib migration + upgrade rule `needsMigration: true`) and carried on every event.

## 4. Backend subscription

```csharp
// durable: outbox row per change, after commit, retries, once per handler, survives restarts and scale-out
services.AddCoworkeeEvents(e => e.PublishChanges<Order>());          // opt-in per entity type (or [PublishChanges] on the entity)
public sealed class OrderShipped : IEntityChangeHandler<Order>
{
    public Task HandleAsync(EntityChange<Order> change, CancellationToken ct) => ...;
}
// runtime (in-process, not durable): hosted services, diagnostics, tests
IDisposable sub = events.Subscribe<EntityChange<Order>>(c => ...);
```

- `IEntityChangeHandler<T>` is an `IDomainEventHandler<EntityChange<T>>` underneath (same outbox processor, same metadata). Only opted-in types write outbox rows (mass imports stay cheap).
- Elsa: `Watch<T>(name)` becomes `PublishChanges<T>()` + an Elsa stimulus handler; `Coworkee.Workflows.Elsa.EntityChanged` remains as an adapter for existing workflows.

## 5. Notifications facade

```csharp
await notifier.SendAsync(new NotificationRequest(EventTarget.Permission(HubPermissions.Orders.View), "Title", "Body")
{
    Delivery = Delivery.Stored | Delivery.Toast,   // default Stored (today's behaviour); Toast = transient, no row
    Severity = Severity.Warning,                   // toast look
    Mail = MailDelivery.ByPreference,              // Never | Always | WhenOffline | ByPreference (default = today)
    Type = "orders.shipped", Link = "/orders/123", Arguments = [...], EntityType/EntityId (following) as today
});
await notifier.ToastAsync(EventTarget.All, "Maintenance at 18:00", Severity.Warning);   // shorthand
```

- Existing `NotifyAsync`/`NotifyLocalizedAsync`/`PublishAsync(NotificationMessage)` keep working and map onto `SendAsync` (user ids → `EventTarget.Users(ids)`).
- `Delivery.Toast`: realtime event `notification.toast` (title, body, severity, link, localized arguments) to the resolved target; the client shell (Coworkee.Client.Blazor) shows it as a MudBlazor snackbar automatically (opt-out option), localized on the client.
- `MailDelivery.WhenOffline`: `RealtimePresence` decides per user at send time; keeps the delay/debounce of types.
- Recipients: target ∪ followers (today's rule) minus actor unless addressed directly; preferences still apply for stored/mail (`Off` wins).

## 6. Blazor subscription

```csharp
await using var s = await Realtime.On<EntityChange<OrderDto>>(c => c.Actor.UserId != me ? ReloadAsync() : Task.CompletedTask);
<RealtimeEvent T="EntityChange<OrderDto>" OnEvent="Reload" />          // component form
Realtime.On<ToastEvent>(...)                                          // custom typed events: [EventName("...")] records
```

- Typed `On<T>` maps the stable event name and topic (`EntityChange<T>` → `type:{Entity}`), deserializes the payload, keeps the reference-counted subscriptions, reconnect and resubscribe of `RealtimeClient`.
- Server side custom typed events: `realtime.PublishAsync(EventTarget, new MyEvent(...))` with `[EventName]`.
- Payload with DTOs (in-place update like the old `UpdateOnChange`): out of scope for now (phase 4, opt-in only, needs row-level visibility).

## Phases (each shippable, each test-first)

1. Envelope metadata; `EventTarget` with ids, `All`, `Current`, `Except`, `|`, predicate over `EventUser` with `HasPermission`/`IsInRole`/`InGroup`/`IsOnline` translated to SQL; `IEventTargetResolver`; `INotifier.SendAsync` with `Delivery` (Toast/Stored), `Severity`, `MailDelivery.WhenOffline`; client toast handler. Docs EN+DE, changelog, samples.
2. `EntityChangeCollector` + sinks (audit, realtime, webhooks, Elsa become sinks, byte-compatible); `ChangeReason` + `AuditEntry.Reason` (migration, upgrade rule); `PublishChanges<T>()` + `IEntityChangeHandler<T>` + runtime `Subscribe<T>`.
3. Typed Blazor `On<T>`/`<RealtimeEvent>`, server typed custom events with `EventTarget`; CoShipee: `HubAudience` → `IAudienceResolver` (follow-up in the app).
4. (later, optional) DTO payloads with in-place update.

## Tests (write first)

- Resolver equals `IPermissionChecker` for a seeded matrix (roles, direct grants, organisation units with ancestors, tenant features, inactive users, other tenant).
- Predicate translation: whole predicate runs in SQL (assert no client evaluation), clear error for untranslatable parts.
- Realtime target: online-only resolution, `user:{id}` delivery, `All` uses the tenant group, `Except(Current)`, tenant isolation, unauthorized topic still refused.
- Toast: no notification row, snackbar shown by the shell (bUnit), localization.
- Mail: Never / Always / WhenOffline (presence on/off) / ByPreference, delay kept.
- Collector: one scan; each sink gets the same changes; legacy records byte-equal to before (golden tests captured BEFORE the refactor); rollback sends nothing; soft delete = Deleted; masked values.
- Reason: scope and request attribute land on audit + events.
- Durable handlers: only opted-in types write outbox rows; handler runs once after commit; retry; metadata present.
- Typed client `On<T>`: deserialization, own-change filter, resubscribe after reconnect.
- Performance guard: SaveChanges of 1,000 entities not slower than before (benchmark test with a tolerance).

## Phase 1 as built (2026-11-20, branch `feat/unified-events`)

Decisions taken while building, and where phase 1 deviates from the text above:

- **Placement.** `EventTarget`, `EventUser`, `IAudienceResolver`, `IEventTargetResolver` live in `Coworkee.Application` (`Coworkee.Application.Events`); the resolver is implemented in `Coworkee.Identity` (it owns the role/grant/group tables and already references Realtime for presence). The target's parts stay internal (`InternalsVisibleTo` Identity and Notifications), so the public surface is the factories, `|`/`Or` and `Except` only.
- **Resolution.** `UsersAsync(target, among)` and `TopicsAsync(target)`. Targets made of ids, `Current`, `|` and `Except` resolve in memory (no database, as `NotifyAsync` always did; user ids are taken as given for stored notifications). Everything else becomes one `Expression<Func<EventUser,bool>>` (ids as list parameters, custom audiences asked first) that Identity rewrites to `User` with the markers as subqueries, tenant + active users, one SQL statement. `IsOnline()` becomes a list parameter from `RealtimePresence`. Member or marker misuse throws `NotSupportedException`, EF translation failures are rethrown as `InvalidOperationException` ("cannot run in the database").
- **HasPermission semantics** = `IPermissionChecker` for roles (direct and of groups), system admin, user and group grants, implications, tenant-specific grants, inactive users, other tenants; proven by a theory over a seeded matrix. Like `UsersGrantedAsync` it ignores computed permissions, grants on single resources and the active role choice. The checker consults no tenant features, so there is nothing feature-specific to mirror.
- **`Current`** is the session topic (`sid` of the request) in realtime/toasts, the user topic without a session (jobs), and the current user in user resolution and inside `Except`.
- **`Global`** is realtime/toast only; user resolution refuses it with a clear message. `All | x` normalizes to `All`, `Global | x` to `Global`.
- **New topics** `tenant:events` and `global:events` (open to every signed-in user of the tenant / of all, existing authorizers). Per-user delivery goes to `user:{id}`, one publish per online user (`ponytail:` comment; a group send later if needed).
- **Realtime API for targets** is a new scoped `RealtimeEvents.PublishAsync(target, type, payload)` (phase 3's typed `PublishAsync(EventTarget, new MyEvent())` belongs on it). With pending changes or an open transaction it enqueues into the scope's `RealtimeChangeInterceptor` (after commit, dropped on rollback), otherwise sends at once. `IRealtimePublisher` is unchanged, so app decorators keep working and see every targeted message per topic.
- **Envelope metadata**: init properties on `RealtimeEnvelope` (`Id` = UUIDv7, `At` from `TimeProvider`, `Actor`, `CorrelationId`), filled by `RealtimePublisher` from the ambient user or the request; `ActorInfo.Name` is the token's `name` claim (no lookup), `Actor` is null for the system. **`CorrelationId` is a string** (the W3C trace id, as `OutboxMessage.CorrelationId`), not a Guid; phase 2's `EntityChange` should use the same.
- **Severity** is `NotificationSeverity` (Contracts) instead of `Severity`: Blazor apps import MudBlazor, whose `Severity` would clash. `Delivery` and `MailDelivery` stay as named (server only).
- **Notifier.** `SendAsync(NotificationRequest)`; `PublishAsync`/`NotifyAsync`/`NotifyLocalizedAsync` keep their code path (stored + `ByPreference`), so their behaviour is byte-for-byte the old one. Target users count as addressed directly (preference scope not Off); the actor is dropped unless named by id or as `Current`. `Mail` is independent of `Delivery`; `ToastAsync` uses `Mail = Never`. `Always` mails everyone not Off, `WhenOffline` asks presence once per send; the type's delay/debounce applies to every mode. Toasts go through `RealtimeEvents` after the rows are added, so they leave with the commit.
- **Decorators.** `INotifier.SendAsync` and `ToastAsync` have default implementations: the default of `SendAsync` maps stored, `ByPreference`, user-id requests onto `PublishAsync` and throws `NotSupportedException` otherwise. Upgrade rule 0.0.1.96 tells decorators (CoShipee's `HubNotifier`) to forward `SendAsync`.
- **Client.** `ToastNotifier` follows `user:{me}`, `tenant:events`, `global:events` and `session:{sid}`, shows `notification.toast` as a MudBlazor snackbar (texts through `L.Server(text, arguments)` like notifications, link as *Open* action); `CoworkeeAppShell` places it for signed-in users unless `CoworkeeClientOptions.ShowToasts` is false.
- **Not in phase 1:** extended/profile attributes on `EventUser`; a session test through a real HTTP request (the session topic path is covered by code, the fallback by a test); TypeScript helpers beyond the types.

## Phase 2 as built (2026-11-20)

- **Golden tests first** (commit 21fbc13a, green on the old code): `Coworkee.Testing.Golden.Match` (golden files next to the test, GUIDs as `<guidN>` in order of appearance, `Version=*`). Captured: the audit trail (create/update/owned-only/not-audited-only/same value/soft delete/restore/hard delete, masking, owned JSON, `[NotAudited]`, `IAnonymous`, `IVersioned`), realtime messages (`[Realtime]`, derived type, soft/hard delete, `IHasRealtimeTopics`, a topic mapper with `tenant:permissions`), webhook outbox rows and Elsa outbox rows (soft delete = "Updated"). All four stayed byte-equal after the refactor.
- **One scan, two hooks.** `EntityChangeCollector` sits exactly where `AuditTrailInterceptor` sat (after soft delete, audit fields, tenant; before snapshots and outbox) and scans `ChangeTracker.Entries()` once into `EntityChangeEntry(EntityChange, EntityEntry)`. `EntityChangeSinks`, the last interceptor, hands that list to the ordered `IEntityChangeSink`s (`Saving` in the save, `CommittedAsync` after commit / save without transaction, `Discarded` on failure/rollback). Two hooks were needed because the goldens proved the sinks see different moments: the audit trail recorded `Revision` before `SnapshotInterceptor` raised it, realtime/webhooks/Elsa read the entries after every interceptor. The entries are live, so the late sinks still read the late state; only rows added between the two hooks (snapshots, outbox, security events: infrastructure types none of the sinks handles) are not in the list.
- **One action mapping** on `EntityChange` (audit's rule: soft delete = Deleted when `IsDeleted` really changed, undoing it = Restored). The legacy adapters keep their own tiny rules where the old payloads differ (realtime/webhooks: Restored = Updated and "IsDeleted modified" without comparing values; Elsa: soft delete = Updated; audit: `ClrType.Name` and the `IMultiTenant` tenant), so their output stays byte-equal. `EntityChange.EntityType` is the root type name (realtime topics), `EntityId` the audit key format, `TenantId` the `TenantId` property or the current tenant.
- **Values** (`PropertyChange`) are computed only for audited entries, with the audit rules, once; the audit sink converts them into `AuditChange`s. Non-audited types carry no values.
- **`EntityChange<TEntity>`** is a sealed record deriving from `EntityChange` (copy constructor, no typed id: the key type is not known generically; deviation from the spec's "EntityId typed").
- **Durable**: `PublishChangesSink` (built in, a cached type lookup per entry) writes an outbox row `EntityChange<T>` for types from `AddCoworkeeEvents(e => e.PublishChanges<T>())` or `[PublishChanges]` (nearest published base type counts). `IEntityChangeHandler<T> : IDomainEventHandler<EntityChange<T>>`, found by `AddMessagingFromAssembly`; the outbox processor gives retries and the actor scope.
- **Runtime**: `IEntityEvents.Subscribe<EntityChange>` / `<EntityChange<T>>` (singleton, immutable list); `EntityEventsSink` keeps the save's changes only while someone subscribes and delivers after commit; failures are logged.
- **Elsa**: `Watch<T>(name)` is unchanged and is now the Elsa sink of the collector (legacy `EntityChanged` payload kept for running workflows). It does not go through `PublishChanges` because that would change the outbox row type and break the golden/running workflows.
- **Reason**: `ChangeReason` (AsyncLocal) + `IHasChangeReason` (the unit-of-work middleware opens the scope around handler and save). `AuditEntry.Reason` (max 500, truncated), `AuditEntryDto.Reason`, `EntityHistoryEntryDto.Reason`, shown in `AuditTimeline` and `CoworkeeEntityHistory`. Migration: apps add one (upgrade rule 0.0.1.96 `needsMigration: true`); the template's initial migration is not regenerated in this branch (it also lacks `cw.ClientValues` of 0.0.1.95; regenerate at release).
- **Performance**: `ChangePerformanceTests` (insert + update of 1,000 audited documents, best of 3): before 896-941 ms, after 808-925 ms on the dev machine; the guard allows 2.5x the 900 ms baseline. Apps without published types or subscribers pay one list per save instead of four scans.
- **ActorInfo.Name** is null on `EntityChange` (no lookup in the save); realtime envelopes still carry the token's name.
