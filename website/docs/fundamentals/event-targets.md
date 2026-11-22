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
| `EventTarget.Users(selection)` | the users of a `UserSelection` (filter of the Users set, added, left out) with the rights of the current user ([below](#user-selections)) |
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

## User selections

A `UserSelection` is what someone chose in a users table: everyone the filter matches (`AllMatching`, `Filter` is an OData `$filter` of the Users set, null for all), plus `Included`, minus `Excluded` (at most 1,000 ids each, `UserSelection.MaxIds`). It is a plain serialisable record: store it, send it, resolve it later. A big choice travels as a filter, never as thousands of ids through the browser.

```csharp
var selection = new UserSelection("Groups/any(g: g/Id eq 6a1f...)", Excluded: [bobId]);
await notifier.SendAsync(new NotificationRequest(EventTarget.Users(selection), "Inventory starts on Monday"), ct);

UserSelection.Of(aliceId, carlId);   // only these users, if the caller may see them
```

The server resolves it with the rights of the **current user**, as the Users set shows them: `Identity.Users.View`, the organisation, the set's row filters and the group selection; a filter he may not run (a hidden property, an unknown name) is refused. Ids the browser added only count when the caller sees those users. As a target the selection reaches the active users of the organisation and follows the target rules: the actor is left out (the selection does not name him by id), preferences apply. Without the right to view users `EventTarget.Users(selection)` throws; `IUserSelectionResolver.ResolveAsync(selection)` returns the ids or the error (`Forbidden`, `Validation`) for own code.

`POST /api/v1/identity/users/selection/count` (`Identity.Users.View`, body: the selection) answers `{ "users": 12, "active": 11 }` (`ICoworkeeApi.CountUserSelectionAsync`).

### Choosing users in Blazor

`CoworkeeUserSelection` (`Coworkee.Client.Blazor.Components.Users`) is a data table over the Users set with its facets, search and column filters: the switch *Everyone matching the filter* takes every match, the checkbox of a row leaves a user out or adds him, *Add a user* adds anyone by name. Next to the facets of the Users set it has the facets **Roles**, the members of roles given directly or through a group that has the role (as permissions count roles), and **Groups**, the members of groups and of their sub-groups (as a tree); each where the user may read roles or groups. Below it shows the count of the server, live: a change of users reloads the table and counts again.

```razor
<CoworkeeUserSelection @bind-Value="_recipients" CountChanged="c => _count = c" Filter="IsActive eq true" />

@code {
    private UserSelection _recipients = new();
    private UserSelectionCountDto? _count;
}
```

They are facets of the page (`PageFacets` of the data table); a chosen role or group is an OData filter of the selection: `(Roles/any(r: r/RoleId eq ...) or Groups/any(m: m/GroupId in (...)))` and `Groups/any(m: m/GroupId in (...))` (the Users set has the navigation `Roles` for this, the direct roles of a user). `Filter` narrows the table and becomes part of the selection; `PageSize` sets the rows per page. The value goes to the server as it is, e.g. in an own request with `EventTarget.Users(request.Recipients)`.

## Sending notifications from the administration

The page *Send notification* (Administration → Communication, `/admin/notifications/send`, page key `send-notification`, permission `Notifications.Send`) sends a notification to a user selection: title, text, optional arguments (one per line: title and text become English texts with `{0}` placeholders that every reader sees in his language), link (a path of the app or an http(s) address), severity, inbox and/or toast, mail (as each recipient chose, never, always, when offline) and the type (the generic one or a registered type). Choosing the recipients also needs `Identity.Users.View`; the system admin role has both. The page sends through `INotifier.SendAsync`, so the rules above apply: the sender is left out, preferences hold, a toast reaches whoever is online at once.

The same from code or another client: `POST /api/v1/notifications/send` with a `SendNotificationRequest` (`ICoworkeeApi.SendNotificationAsync`):

```json
{ "recipients": { "filter": "contains(Email,'@acme.test')", "allMatching": true, "excluded": ["..."] },
  "title": "Maintenance at {0}", "body": "Please save your work.", "arguments": ["18:00"],
  "link": "/status", "severity": "Warning", "stored": true, "toast": true, "mail": "WhenOffline", "type": null }
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

## Typed events

Give an event type a stable name and publish it to a target; the name survives renaming or moving the type (without the attribute it is the short type name, never the full CLR name):

```csharp
[EventName("orders.approved")]
public sealed record OrderApproved(Guid OrderId, string Number);   // in the contracts, shared with the client

await events.PublishAsync(EventTarget.Permission(OrderPermissions.View), new OrderApproved(order.Id, order.Number), ct);
```

In Blazor `RealtimeClient.On<T>` subscribes, deserializes and keeps the reference-counted subscriptions, reconnects and resubscribes of `SubscribeAsync`:

```csharp
// an event of the server: on every topic a target reaches this user on (his own, tenant:events, global:events, his session)
await using var approved = await Realtime.On<OrderApproved>(e => InvokeAsync(() => Snackbar.Add(L["Order {0} approved", e.Number])));

// entity changes: OrderDto follows type:Order (the name without "Dto"), with who and when; skipOwn leaves out the user's own changes
await using var changes = await Realtime.On<EntityChanged<OrderDto>>(c => InvokeAsync(ReloadAsync), skipOwn: true);
await using var one = await Realtime.On<EntityChanged<OrderDto>>(c => c.IsDeleted ? GoBackAsync() : ReloadAsync(), topic: RealtimeTopics.Entity("Order", id));
```

```razor
<RealtimeEvent T="EntityChanged<OrderDto>" OnEvent="ReloadAsync" SkipOwn="true" />
<RealtimeEvent T="OrderApproved" OnEvent="ShowApproved" />
```

`EntityChanged<TDto>` carries `EntityType`, `EntityId`, `Action`, `ChangedProperties`, `Actor` and `At`, never values: the client reloads what it may see. `ToastPayload` has its name too (`On<ToastPayload>` is what the shell's `ToastNotifier` uses). In TypeScript `onEvent<T>(realtime, 'orders.approved', targetedTopics(userId, sessionId), handler, { skipActor: userId })` does the same.

## Who, when, why

Every `RealtimeEnvelope` carries metadata the publisher fills, without a lookup: `Id` (unique per message), `At` (when it was sent), `Actor` (`ActorInfo(UserId, Name, ImpersonatorId)`; null for the system) and `CorrelationId` (the trace of the request). A component can ignore its own changes or say who made one:

```csharp
_subscription = await Realtime.SubscribeAsync(RealtimeTopics.Type("Order"), envelope =>
    envelope.Actor?.UserId == _me ? Task.CompletedTask : InvokeAsync(ReloadAsync));
```

Older clients ignore the new fields; `@coworkee/client` types them (`RealtimeEnvelope.actor`, `Toast`).

## Toasts in the client

`Delivery.Toast` sends `notification.toast` (`ToastPayload`: title, body, severity, link, arguments, type) to the target, without a row and without a mail. `CoworkeeAppShell` (and so `CoworkeeLayout`) shows it as a snackbar for signed-in users, translated like notifications (with `Arguments`, also empty, title and body are English texts with `{0}` placeholders) and with the link as *Open* action. `options.ShowToasts = false` turns that off; the app then places `<ToastNotifier />` itself or handles `ToastPayload.Event` on its own.
