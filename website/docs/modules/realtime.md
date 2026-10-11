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

A message goes as `chat.message` to the topics `user:{id}` of both people, reading a conversation as `chat.read`. The app bar's `ChatNotifier` shows a snackbar with the sender, the start of the text and a link to `/chat?with={sender}`; the chat page keeps the open conversation in that address, so it stays quiet while you are in it. It also puts the unread count (`GET /api/v1/chat/unread-count`) on the chat link of the navigation and keeps it live in every tab.

Any page can show a count on its navigation link the same way: `NavigationBadges.Set("/orders", 3)`.

## Notifications

```csharp
await notifier.NotifyAsync([reviewerId], "review.requested", "Review requested", $"{asset.Name} waits for you", link: $"/assets/{asset.Id}", ct);
```

Localizable notifications take the English texts as keys with `{0}` placeholders and the arguments apart; every reader sees them in his language (the texts belong into the app's translations), also in the digest mail, which takes the reader's `Localization.Culture` (his own, else his organisation's, else the system's):

```csharp
await notifier.NotifyLocalizedAsync(adminIds, "account.registration", "New registration", "{0} ({1}) waits for activation.", [name, email], $"/admin/users/{id}", ct);
```

Users see them in the bell of the app bar and on the notifications page, live through SignalR. A daily digest mails unread ones (`Coworkee:Notifications:DigestCron`), with links to `PublicAppUrl`.

The bell shows the unread count and the latest ten; a notification that arrives pops up as a snackbar with a link to it. Clicking one marks it read and opens its link (only pages of the app). Each can be marked read or unread again and deleted; the bell marks all as read, the page `/notifications` (also a tab of `/profile`) pages through all or only the unread ones and deletes all. Every change reaches all open tabs of the user at once: the client's `NotificationCenter` holds the unread count and raises `Changed` once per burst, also after "mark all as read".

| Endpoint | |
|---|---|
| `GET /api/v1/notifications?unreadOnly=&type=&page=&pageSize=` | newest first |
| `GET /api/v1/notifications/unread-count` | |
| `POST /api/v1/notifications/{id}/read`, `/{id}/unread` | |
| `POST /api/v1/notifications/read-all` | |
| `DELETE /api/v1/notifications/{id}`, `DELETE /api/v1/notifications` | one, all |
