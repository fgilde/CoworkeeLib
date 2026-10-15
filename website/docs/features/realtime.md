---
title: "Realtime updates and collaboration for Blazor apps"
description: "SignalR realtime for ASP.NET Core and Blazor apps: live tables and detail pages, presence, notifications, chat, comments, background jobs and a Redis backplane."
---

# Realtime collaboration for ASP.NET Core and Blazor

Business apps are used by several people at once. A colleague changes an order while you look at it, an admin takes away a role, a reviewer waits for a file. If every screen only shows what was true when it was loaded, people work on stale data, overwrite each other and keep pressing F5.

In a Coworkee app every open tab follows these changes through one SignalR hub. Tables reload when their entity changes, a detail page notices that its entry was deleted, the navigation adapts when permissions change, and messages and notifications arrive while the user is still on the page. Most of it needs one attribute on the entity and nothing in the page.

![Chat page with the people of the organisation and an open conversation](../assets/screenshots/chat.png){ .shot loading=lazy }

## Entity changes pushed to the UI over SignalR

Mark an entity with `[Realtime]` and the permission needed to see it. Every committed insert, update and delete (a soft delete counts as a deletion) goes to the users of the tenant who hold that permission:

```csharp
[Realtime(CatalogPermissions.Brands.View)]
public sealed class Brand : AuditedEntity, IMultiTenant { }
```

A `CoworkeeDataTable` of `BrandDto` follows `Brand` by itself and reloads once per burst of changes. A detail page watches its entry with one component, and anything else subscribes to a topic:

```razor
<EntityWatch EntityType="Brand" EntityId="Id" ReturnHref="/catalog/brands" OnChanged="LoadAsync" />
<RealtimeSubscription Topic="type:Brand" OnEvent="_ => LoadAsync()" />
```

Data that only some users may see gets its own topics, published with `PublishAsync` of `IRealtimePublisher`. Bulk changes through `ExecuteUpdate` or `ExecuteDelete` bypass the change tracker, so you publish those yourself as well.

## Permissions that change while people are signed in

Saving a role, a grant or a group membership publishes a permission change. The server drops its cached permissions after the commit, the client reloads them, and navigation, `AuthorizeView` and the page guard evaluate again. A page the user may no longer open turns into "not allowed" without a reload. The server also takes back topic subscriptions a user may no longer follow, so change notices of an entity type stop reaching someone who lost the permission to view it.

## Active role

A user with several roles can act within one of them, chosen in the app bar. While one is active, only that role's permissions count, in the UI and in every API permission check. When a page is not allowed, the `NotAllowed` component lists the user's other roles that would grant it and switches with a click.

## Presence, notifications and chat

- **Presence**: a user with at least one open connection is online, shown as a green dot on every `UserAvatar`. Closing the last tab turns them offline after a few seconds, so a reload does not flicker.
- **Notifications**: modules send them with `NotifyAsync` or, translated per reader, `NotifyLocalizedAsync`. They show up in the bell of the app bar and on `/notifications`, live in every tab, and a daily digest mails the unread ones.
- **Chat**: `Coworkee.Chat` adds direct messages between the people of an organisation. A new message shows a snackbar with a link unless the conversation is open, and unread counts stay in sync across tabs.

```csharp
await notifier.NotifyAsync([reviewerId], "review.requested", "Review requested",
    $"{asset.Name} waits for you", link: $"/assets/{asset.Id}", ct);
```

## Comments, tags and ratings on any entity

`Coworkee.Social` attaches threaded comments, tags and 1 to 5 star ratings to entities you opt in. Each block has its own view, create and moderate permissions, comment threads reload live over the hub, and the people involved get a notification when someone replies.

```csharp
context.Services.AddCoworkeeSocial(social => social
    .Comments<Product>("Products", id => $"/products/{id}")
    .Tags<Product>("Products")
    .Ratings<Product>("Products"));
```

In the page, `CoworkeeComments`, `CoworkeeTags` and `CoworkeeRating` take the entity type and id and hide what the user may not do. `CoworkeeTagCloud` turns a chosen tag into an OData filter for a data table.

## Mailing and campaigns

Mails are Scriban templates with a model and per-culture defaults. Administrators override subject and body in a visual or source editor, create their own templates and send campaigns to smart lists or a manual selection of users, now or scheduled. Sending is queued, retried and logged. Mail goes out through SMTP or one of the HTTP providers, and an organisation can use its own server.

## Background jobs

Jobs run on Hangfire with the storage of the database provider. An enqueued job runs as the user who enqueued it, so permissions, tenant filters and audit work as in a request. Recurring jobs take a cron expression:

```csharp
jobs.Enqueue<RenderPreviewJob, RenderPreview>(new RenderPreview(asset.Id), queue: "processing");
services.AddRecurringJob<CleanupJob>("cleanup", "0 3 * * *");
```

Users with the jobs permission find the Hangfire dashboard under `/admin/jobs`.

## Scale-out with a Redis backplane

One instance needs nothing extra. With several API instances, a `redis` connection string turns on the SignalR backplane, so an event reaches the connections of every instance. The same connection keeps presence counting a user once, invalidates permission, setting and text caches on all instances, and carries session changes. The Aspire app host adds a Redis container for every service that uses realtime; `UseRedis(...)` points it at an existing server and `WithoutRedis()` leaves it out. A Redis that stops answering is not fatal: the services log a warning and fall back to local state.

## Where to go next

- [Realtime and notifications](../modules/realtime.md): topics, the active role, presence and the notification endpoints
- [Chat](../modules/chat.md): the chat page, its endpoints and live messages in your own components
- [Comments, tags and ratings](../modules/social.md): registration, permissions and client components
- [Mailing](../modules/mailing.md): templates, campaigns and mail providers
- [Background jobs](../modules/background-jobs.md): jobs, queues and recurring jobs
- [Scaling out with Redis](../hosting/redis.md): what Redis shares between instances
- [Clients and sessions](../security/clients.md): how ended sessions reach every tab
