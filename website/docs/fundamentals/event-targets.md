# Events and targets

Who gets something that happens, a toast, a notification, a mail or a realtime event, is one value: an `EventTarget`. The same target works in the [notifier](../modules/realtime.md#notifications) and for realtime events, resolved in the current organisation.

```csharp
await notifier.SendAsync(new NotificationRequest(EventTarget.Permission(OrderPermissions.View).Except(EventTarget.Current), "Order {0} shipped")
{
    Arguments = [order.Number],
    Link = $"/orders/{order.Id}",
    Delivery = Delivery.Stored | Delivery.Toast,
    Severity = NotificationSeverity.Success,
    Mail = MailDelivery.WhenOffline,
}, ct);

await notifier.ToastAsync(EventTarget.All, "Maintenance at 18:00", NotificationSeverity.Warning, ct);
```

## Targets

| Target | Who |
|---|---|
| `EventTarget.All` | every user of the organisation |
| `EventTarget.Global` | every user of every organisation: realtime and toasts only (system messages); stored notifications refuse it |
| `EventTarget.Current` | the current session (all tabs of this sign-in) in realtime and toasts, the current user everywhere else and inside `Except` |
| `EventTarget.Users(id1, id2)` | these users |
| `EventTarget.Permission("Orders.View")` | the users who hold the permission |
| `EventTarget.Role("Dispatcher")` | the users with the role, directly or through a group |
| `EventTarget.Group(groupId)` | the members of the group and of its sub-groups |
| `EventTarget.Users(u => ...)` | the users who match a predicate (below) |
| `EventTarget.Custom<TResolver>(args)` | the users an `IAudienceResolver` of the app names |
| `a \| b`, `a.Or(b)` | the users of both |
| `a.Except(b)` | the users of `a` without those of `b`, e.g. `.Except(EventTarget.Current)` to leave out the one who acted |

### Predicates

A predicate over `EventUser` (`Id`, `UserName`, `Email`, `FirstName`, `LastName`) runs in the database as **one query**, with four markers that become subqueries over the library's roles, grants and groups:

```csharp
EventTarget.Users(u => u.InGroup(salesId) && u.HasPermission(OrderPermissions.Approve) && !u.IsInRole("Trainee"))
EventTarget.Users(u => u.Email!.EndsWith("@partner.example") && u.IsOnline())
```

| Marker | Meaning |
|---|---|
| `HasPermission(name)` | as `IPermissionChecker`: a role (direct or through a group) with the permission or one implying it, the admin role, a grant of the user or of one of his groups (a group's grant does not reach its sub-groups). Computed permissions, grants on single resources and the active role choice do not count |
| `IsInRole(name)` | has the role, directly or through one of his groups |
| `InGroup(id)` | member of the group or of one of its sub-groups |
| `IsOnline()` | has a realtime connection now |

The arguments of the markers are values (constants, variables), never something of the user. Anything the database cannot run fails at once with a message that names it; nothing is evaluated user by user in memory.

### Audiences of the app

An audience the library cannot know, such as the parties of a shipment, is an `IAudienceResolver`; register it or not (an unregistered one is created with its dependencies):

```csharp
public sealed class ShipmentAudience(AppDbContext db) : IAudienceResolver
{
    public async Task<IReadOnlyCollection<Guid>> ResolveAsync(object? args, CancellationToken ct) =>
        await db.ShipmentParties.Where(p => p.ShipmentId == (Guid)args!).Select(p => p.UserId).ToListAsync(ct);
}

await notifier.SendAsync(new NotificationRequest(EventTarget.Custom<ShipmentAudience>(shipment.Id), "Shipment departed"), ct);
```

## How targets resolve

`IEventTargetResolver` (Coworkee.Identity implements it) resolves in the current organisation; a target only narrows:

- **Realtime and toasts** (`TopicsAsync`): `All` goes once to the organisation's topic `tenant:events`, `Global` to `global:events`, `Current` to the session's topic (`session:{sid}`, the user's own topic without a session, e.g. in a job). Every other target asks [presence](../modules/realtime.md#presence) for the online users of the organisation first and then runs the target only for them; each gets the event on his `user:{id}` topic. A big organisation costs what its online users cost, and users who are offline cost nothing.
- **Stored notifications and mails** (`UsersAsync`): active users of the organisation in one query. User ids (also with `Current` and `Except` of ids) need no database and are taken as given, as `NotifyAsync` always did.

Users of other organisations and inactive users never resolve, also not from an `IAudienceResolver`. The topics keep their subscribe-time authorization: a client follows only its own user and session topics, `tenant:events` of its own organisation and `global:events`.

## Realtime events for a target

`RealtimeEvents` publishes to a target instead of a topic. Inside a unit of work with pending changes the event leaves after the commit (none on a rollback), otherwise at once:

```csharp
internal sealed class ApproveOrderHandler(AppDbContext db, RealtimeEvents events) : IHandler<ApproveOrder, Result>
{
    public async Task<Result> HandleAsync(ApproveOrder command, CancellationToken ct)
    {
        var order = await db.Orders.SingleAsync(o => o.Id == command.Id, ct);
        order.Approve();
        await events.PublishAsync(EventTarget.Permission(OrderPermissions.View).Except(EventTarget.Current), "orders.approved", new { order.Id }, ct);
        return Result.Success();
    }
}
```

Clients receive it on the topics they follow anyway (`user:{id}`, `tenant:events`, `global:events`, `session:{sid}`); a Blazor page subscribes one of them with `RealtimeClient.SubscribeAsync` and checks the `Type`. Like every realtime payload it goes to everyone the target names: send ids and what changed, not data some of them must not see.

## Who, when, why

Every `RealtimeEnvelope` carries metadata the publisher fills, without a lookup: `Id` (unique per message), `At` (when it was sent), `Actor` (`ActorInfo(UserId, Name, ImpersonatorId)`; null for the system) and `CorrelationId` (the trace of the request). A component can ignore its own changes or say who made one:

```csharp
_subscription = await Realtime.SubscribeAsync(RealtimeTopics.Type("Order"), envelope =>
    envelope.Actor?.UserId == _me ? Task.CompletedTask : InvokeAsync(ReloadAsync));
```

Older clients ignore the new fields; `@coworkee/client` types them (`RealtimeEnvelope.actor`, `Toast`).

## Toasts in the client

`Delivery.Toast` sends `notification.toast` (`ToastPayload`: title, body, severity, link, arguments, type) to the target, without a row and without a mail. `CoworkeeAppShell` (and so `CoworkeeLayout`) shows it as a snackbar for signed-in users, translated like notifications (with `Arguments`, also empty, title and body are English texts with `{0}` placeholders) and with the link as *Open* action. `options.ShowToasts = false` turns that off; the app then places `<ToastNotifier />` itself or handles `ToastPayload.Event` on its own.
