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
