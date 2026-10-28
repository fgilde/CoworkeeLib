# Realtime and notifications

## What arrives live

Every open tab follows these changes without a reload:

| Change | What the client does |
|---|---|
| A `[Realtime]` entity is created, changed or deleted (also soft deleted) | every `CoworkeeDataTable` of that entity reloads; a detail page with `EntityWatch` reloads, or says the entry is gone and goes back |
| Roles, role grants, group memberships or the active role change | the client reloads its permissions: navigation, `PermissionView`, `AuthorizeView` and the page guard evaluate again, so a page turns into "not allowed" or back into the page |
| A user is deactivated, locked, signed out or changes the password | his sessions end (see [Clients and sessions](../security/clients.md)) |
| The active role setting is switched | every client reloads its permissions |
| Someone sends a chat message | the recipient sees a snackbar with a link to the conversation, unless it is open; an open conversation shows it at once |
| A conversation is read | unread counts follow in every tab (also the count on the chat link), the sender sees the read mark |
| Someone comes online or goes offline | a green dot on every avatar of that user |
| Someone changes name or picture | every avatar and user card showing that user updates |
| A notification arrives or changes | bell, count and notifications page follow |
| Features, languages, themes | feature gates, language menu and theme lists follow |

## Entity changes

Mark an entity, and every committed insert, update and delete is pushed over SignalR to the users of the tenant who hold the permission. A soft delete is reported as a deletion:

```csharp
[Realtime(CatalogPermissions.Brands.View)]
public sealed class Brand : AuditedEntity, IMultiTenant { }
```

A `CoworkeeDataTable` follows its entity by itself: the topic is `type:` plus the row type without `Dto`, so a table of `BrandDto` reloads on every change of `Brand`, once per burst (a bulk delete sends one event per row). Set `RealtimeTopic` when the row type is named differently, or to an empty string to turn it off:

```razor
<CoworkeeDataTable T="UserRow" EntitySet="Users" RealtimeTopic="type:User" ... />
```

A detail page watches its entry:

```razor
<EntityWatch EntityType="Brand" EntityId="Id" ReturnHref="/catalog/brands" OnChanged="LoadAsync" />
```

Anything else subscribes to a topic with `RealtimeSubscription`; it follows a changed `Topic`:

```razor
<RealtimeSubscription Topic="type:Brand" OnEvent="_ => LoadAsync()" />
```

`ExecuteUpdate` and `ExecuteDelete` bypass the change tracker, so nothing is published for them. Tell the clients yourself after such a bulk change:

```csharp
await publisher.PublishAsync(tenantId, RealtimeTopics.Type(nameof(Brand)), RealtimeEventTypes.EntityChanged,
    new EntityChangedPayload(nameof(Brand), "*", EntityChangedPayload.Deleted, []), ct);
```

Private data needs more care: entities only some users may see should not use the type topic. Publish your own topics instead:

```csharp
await publisher.PublishAsync(tenantId, $"folder:{folder.Id}", "AssetAdded", new { asset.Id }, ct);
```

With several API instances, set the connection string `redis` for the SignalR backplane. The app host does that for every service that references `Coworkee.Realtime`. The same connection carries the session changes between the instances (see [Clients and sessions](../security/clients.md)): each instance ends the hub connections of a session that was ended, whichever instance holds them.

## Permissions

Saving roles, role or user grants, group memberships, group roles or the active role publishes on `tenant:permissions`; the setting below publishes on `global:permissions`. The server drops its cached permissions after the commit, the client's `PermissionStore` reloads them once per burst and raises `Changed`, and the authentication state provider tells `AuthorizeView` and `AuthorizeRouteView` to evaluate again. Nothing in a page needs to subscribe.

Topics a user may no longer follow are taken back on the server: after such a change every instance asks the topic authorizers again for the topics its connections follow and removes the connections from the groups they may no longer join, so no change notices of an entity type reach a user who lost the permission to view it (also after switching the active role). The client subscribes its topics again when its permissions grew.

## Active role

A user with several roles chooses one in the app bar ("All roles" or one of them). As long as one is active, his permissions come only from that role: neither his other roles nor grants given to him directly count. The server applies it in every permission check, so the API refuses what the role does not allow; it never grants more than the user's roles do. The choice is stored with the user (a user claim) and follows him to every device; a role taken away from him falls back to all roles.

The system setting **Act within one role** (`Account.ActiveRole`, under Sign-in security) turns it on and off; it is on by default. Without the account module the active role is always allowed.

When a page is not allowed, `NotAllowed` lists the user's other roles that grant the page's permissions and switches to one with a click; the page then appears by itself. The app's router shows it:

```razor
<AuthorizeRouteView RouteData="routeData" DefaultLayout="typeof(CoworkeeLayout)">
    <NotAuthorized>
        <NotAllowed PageType="routeData.PageType" />
    </NotAuthorized>
</AuthorizeRouteView>
```

| Endpoint | |
|---|---|
| `GET /api/v1/identity/roles/me?permission=` | own roles, the active one, whether each grants the permission |
| `PUT /api/v1/identity/roles/me/active` | `{ "roleId": null }` acts with all roles again |

## Presence

A user with at least one hub connection is online, however many tabs he has open; after the last one closes he turns offline after a few seconds, so a reload does not flicker. Changes go as `presence.changed` on `tenant:presence`, `GET /api/v1/realtime/presence` lists who of the organisation is online, and `UserAvatar` shows a green dot (`ShowPresence="false"` hides it). With several API instances a user counts per instance.

## Chat

A message goes as `chat.message` to the topics `user:{id}` of every member of its chat, reading as `chat.read`, writing as `chat.typing`, name and member changes as `chat.conversation` ([Chat](chat.md#live-events)). The app bar's `ChatNotifier` shows a snackbar with the sender and the start of the text whose action opens the chat as mini chat; it stays quiet while that chat is open on the chat page (`/chat?c={id}`) or in the dock. It also puts the unread count (`GET /api/v1/chat/unread-count`) on the chat link of the navigation and keeps it live in every tab.

Any page can show a count on its navigation link the same way: `NavigationBadges.Set("/orders", 3)`.

## Notifications

```csharp
await notifier.NotifyAsync([reviewerId], "review.requested", "Review requested", $"{asset.Name} waits for you", link: $"/assets/{asset.Id}", ct);
```

Localizable notifications take the English texts as keys with `{0}` placeholders and the arguments apart; every reader sees them in his language (the texts belong into the app's translations), also in the digest mail, which takes the reader's `Localization.Culture` (his own, else his organisation's, else the system's):

```csharp
await notifier.NotifyLocalizedAsync(adminIds, "account.registration", "New registration", "{0} ({1}) waits for activation.", [name, email], $"/admin/users/{id}", ct);
```

Users see them in the bell of the app bar and on the notifications page, live through SignalR. A daily digest mails unread ones (`Coworkee:Notifications:DigestCron`), with links to `PublicAppUrl`; notifications that went out as a mail of their own (e-mail channel of a type) are left out.

The bell shows the unread count and the latest ten; a notification that arrives pops up as a snackbar with a link to it. Clicking one marks it read and opens its link (only pages of the app). Each can be marked read or unread again and deleted; the bell marks all as read, the page `/notifications` (also a tab of `/profile`) pages through all or only the unread ones and deletes all. Every change reaches all open tabs of the user at once: the client's `NotificationCenter` holds the unread count and raises `Changed` once per burst, also after "mark all as read".

| Endpoint | |
|---|---|
| `GET /api/v1/notifications?unreadOnly=&type=&page=&pageSize=` | newest first |
| `GET /api/v1/notifications/unread-count` | |
| `POST /api/v1/notifications/{id}/read`, `/{id}/unread` | |
| `POST /api/v1/notifications/read-all` | |
| `GET /api/v1/notifications?category=&done=&search=` | inbox filters: category, open (`done=false`) or done, text in title or body |
| `GET /api/v1/notifications/counts` | open, unread and open per category |
| `POST /api/v1/notifications/{id}/done`, `/{id}/undone` | done also marks read |
| `GET /api/v1/notifications/preferences`, `PUT .../preferences/{type}` | the user's choice per registered type |
| `GET /api/v1/notifications/follows`, `GET/PUT/DELETE .../follows/{entityType}/{entityId}` | followed entities, follow state, follow, unfollow |
| `DELETE /api/v1/notifications/{id}`, `DELETE /api/v1/notifications` | one, all |

### Preferences, following and delayed mails

Apps register the notification types users choose for themselves, and the entity types they may follow:

```csharp
services.AddNotificationTypes(n => n
    .Add("shipment.eta", "Shipment", "Estimated arrival changed", NotificationScope.Followed, inApp: true, email: true, emailDelay: TimeSpan.FromMinutes(1))
    .Add("quotation.new", "Quotation", "New quotation", NotificationScope.All)
    .Followable<Shipment>("Shipments", id => $"/shipments/{id}"));
```

A type has a category (inbox filter and counts), a name (localization key) and defaults: the scope (`Off`, `All`, `Followed`) and the channels (in the app, by e-mail). Each user overrides them per type on the *Preferences* tab of `/notifications` (`NotificationPreference`, table `cw.NotificationPreferences`). Following is opt-in per entity type: with `Followable<TEntity>` the user must see the row through the entity's OData set (403 without the set's permission, 404 for a hidden, foreign or guessed id).

!!! warning "`Followable(string)` checks nothing"
    With `Followable("Type")` users may follow any id, and nobody checks that they may see it. Notifications about such entities therefore reach followers only through an `Audience`, limited to it; without an audience their followers get nothing. Register records some users must not see with `Followable<TEntity>`.
 `CoworkeeFollowButton` (`EntityType`, `EntityId`, `Title`) follows and unfollows on a detail page; the *Following* tab lists what the user follows. `NotificationFollow` (`cw.NotificationFollows`) is public, so apps filter their lists by it (followed only, an is-followed column).

`INotifier.PublishAsync(NotificationMessage)` sends to three kinds of recipients:

```csharp
await notifier.PublishAsync(new NotificationMessage("shipment.eta", "Estimated arrival of {0} changed", "New: {1}")
{
    Arguments = [shipment.Number, eta.ToString("d")],
    Link = $"/shipments/{shipment.Id}",
    EntityType = "Shipments", EntityId = shipment.Id,  // its followers
    Audience = usersWhoMaySeeIt,                        // everybody with the preference All
    Recipients = [shipment.OwnerId],                    // addressed directly
}, ct);
```

Addressed users get it unless their scope is Off; followers when it is All or Followed and they still see the entity: limited to the audience when one is given, otherwise each follower is checked as himself against the set of the `Followable<TEntity>` registration (permission, row filters, groups); the audience when it is All. The actor gets it only when addressed. Per recipient the channels decide: in the app a notification, by e-mail the mail `Notifications.Notification` in the reader's language, only to an active address (without one the notification stays in the daily digest). A type with `emailDelay` lets the mail wait in `cw.PendingNotificationMails`; another notification of the same user, type and entity before that replaces it and waits anew, so a burst of changes ends in one mail with the latest text. The job `coworkee-notification-mails` sends due mails every minute. A waiting mail is unique per user, type and entity and is written through the outbox after the sending command commits: a command that fails sends no mail, and parallel notifications or the job never fail the command (the outbox handler retries; the API host runs the outbox with `AddCoworkeeOutboxProcessing`); the job takes a mail only in the version it read (`Version` changes on every replacement), a mail replaced meanwhile waits anew. Publish inside the command, before its unit of work is saved (in a handler the pipeline saves), or after the commit from a domain event handler: the in-app notifications, immediate mails and the outbox message of a delayed mail are part of that save, so a command that fails sends nothing, and code that publishes after its last `SaveChangesAsync` must save again.

The follower check costs one query per follower without an `Audience`: each follower is checked as himself, because permissions, row filters and groups differ per user. For entities followed by hundreds of users pass an `Audience` (the users who may see the entity), which replaces the per-follower check by one intersection.

`NotifyAsync` and `NotifyLocalizedAsync` are `PublishAsync` with `Recipients`; unregistered types reach them in the app only, as before.

The inbox lists open notifications by default; *Done* moves one out of it (and marks it read), the tabs show open, done or all, the chips filter by category with their open counts, the search looks in title and body.

### Mail templates per type and delay per tenant

A type can mail with a template of its own and take the mail delay from the sender's tenant:

```csharp
services.AddNotificationTypes(n => n
    .Add("shipment.eta", "Shipment", "Estimated arrival changed", NotificationScope.Followed, email: true, emailDelay: TimeSpan.FromMinutes(1),
        mailTemplate: "Shipments.EtaChanged",
        resolveEmailDelay: async (services, ct) =>
            int.TryParse(await services.GetRequiredService<IFeatureChecker>().GetValueAsync("Shipments.MailDelayMinutes", ct), out var minutes)
                ? TimeSpan.FromMinutes(minutes) : null));

await notifier.PublishAsync(new NotificationMessage("shipment.eta", "Estimated arrival of {0} changed")
{
    Arguments = [shipment.Number],
    EntityType = "Shipments", EntityId = shipment.Id,
    MailModel = new { shipment = new { shipment.Number, shipment.PortName }, OldValue = before.ToString("yyyy-MM-dd"), NewValue = after.ToString("yyyy-MM-dd") },
}, ct);
```

`mailTemplate` names a mail template of [mailing](mailing.md) (registered with an `IMailTemplateContributor`, or an own template), so administrators change it on the mail templates page per language and organisation. It renders `MailModel` (an object or a dictionary): its members are the template's top-level names, named like the members of any mail model (`OldValue` is `old_value`), next to `user` (`first_name`, `email`) and `notification` (`title`, `body` in the reader's language, absolute `link`). The library fills those, so `PublishAsync` refuses a model whose top level has a member `user`, `notification`, a variable of the layout (`subject`, `content`, `unsubscribe_url`) or the name of a Scriban builtin (`string`, `html`, `date`, `array`, `math`, `object`, `regex`, `timespan`, ...), in any case and with or without underscores (`ArgumentException`), and also one larger than 32 KB as JSON (`NotificationMailModel.MaxBytes`). The model is stored as JSON with the outbox message and the waiting mail, so a delayed mail renders the model of the latest notification of its entity; values arrive as text, numbers, booleans, lists and objects, so format dates before publishing. Delayed mails render in the organisation that sent them (its template versions, its mail log). A type without a template, or with a name no template has (a warning goes to the log), and a mail without a model (for example one that waited since before its type got a template) send the generic `Notifications.Notification` as before; its model is passed too, so an organisation's version of the generic template can read it. A template that fails to render (for example an edited version the model does not fit) sends the generic mail instead, and if that fails as well the mail is dropped; both are logged, and neither fails the sending command nor the other mails of the job's run.

Give the template's definition `NotificationMailModel.Sample(model)` as its sample: the preview and the test mail on the mail templates page then show the sample model with a sample `user` and `notification`.

```csharp
context.Add(new MailTemplateDefinition("Shipments.EtaChanged", "Estimated arrival changed",
    NotificationMailModel.Sample(new { shipment = new { Number = "S-1", PortName = "Hamburg" }, OldValue = "2026-05-01", NewValue = "2026-05-03" }),
    EmbeddedTemplates.Load(typeof(ShipmentMails).Assembly, "Shipments.EtaChanged")));
```

`resolveEmailDelay` runs when a notification is published, with the services of the sender's scope (its tenant, features and settings). Its result is the delay for this notification; `null` keeps `emailDelay`, `TimeSpan.Zero` mails at once. A resolver that throws is logged and `emailDelay` applies; it never fails the publishing command. The waiting mail keeps the due time it got, a newer notification of the same entity sets a new one.
