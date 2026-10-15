---
title: "Realtime updates and collaboration for Blazor apps"
description: "SignalR realtime for ASP.NET Core and Blazor apps: live tables and detail pages, presence, notifications, chat, comments, background jobs and a Redis backplane."
---

# Realtime and collaboration

A Coworkee app keeps one SignalR connection per tab, and most of what other people do arrives over it. A table reloads when its entity changes. A detail page notices that its record was deleted and goes back to the list. When an admin takes a role away, the navigation shrinks while the user is still looking at it.

Most of this needs one attribute on the entity and nothing in the page.

## Entity changes in the UI

```csharp
[Realtime(CatalogPermissions.Brands.View)]
public sealed class Brand : AuditedEntity, IMultiTenant { }
```

Every committed insert, update and delete of `Brand` now goes to the users of the tenant who hold `Brands.View` (a soft delete counts as a delete). A `CoworkeeDataTable` of `BrandDto` follows it by itself and reloads once per burst, so a bulk delete of 200 rows does not mean 200 reloads. Detail pages and anything else subscribe explicitly:

```razor
<EntityWatch EntityType="Brand" EntityId="Id" ReturnHref="/catalog/brands" OnChanged="LoadAsync" />
<RealtimeSubscription Topic="type:Brand" OnEvent="_ => LoadAsync()" />
```

Two things stay your job. Records only some users may see should go out on topics of their own through `IRealtimePublisher.PublishAsync`, and `ExecuteUpdate`/`ExecuteDelete` skip the change tracker, so nothing is published for them unless you do it.

Permission changes travel the same way. After a role, grant or group membership is saved, the client reloads its permissions and `AuthorizeView` and the page guard evaluate again; the server also drops the user from topics they may no longer follow. A user with several roles can pick one in the app bar and act only within it, and the `NotAllowed` component offers to switch to a role that would open the page.

## Presence, notifications and chat

A user with at least one open connection is online, shown as a green dot on `UserAvatar`. Closing the last tab turns them offline after a few seconds, which keeps a page reload from flickering.

Notifications land in the bell of the app bar and on `/notifications`, live in every tab, with a daily digest mail for the unread ones:

```csharp
await notifier.NotifyAsync([reviewerId], "review.requested", "Review requested",
    $"{asset.Name} waits for you", link: $"/assets/{asset.Id}", ct);
```

`NotifyLocalizedAsync` does the same with English texts as keys, so each reader gets their own language. `Coworkee.Chat` adds direct messages inside an organisation; a new message pops up as a snackbar unless the conversation is already open.

## Comments, tags and ratings

`Coworkee.Social` hangs threaded comments, tags and 1 to 5 star ratings on entities you opt in:

```csharp
context.Services.AddCoworkeeSocial(social => social
    .Comments<Product>("Products", id => $"/products/{id}")
    .Tags<Product>("Products"));
```

Each block has its own view, create and moderate permission. `CoworkeeComments`, `CoworkeeTags` and `CoworkeeRating` take entity type and id and hide the inputs the user may not use, and comment threads reload live.

## Mail, jobs and the Redis backplane

Mails are Scriban templates with per-culture defaults that admins can override. Admins can also write their own and send them as campaigns to smart lists of users. Sending is queued and retried by a background job.

Jobs run on Hangfire, as the user who enqueued them, so tenant filters and audit behave as in a request:

```csharp
jobs.Enqueue<RenderPreviewJob, RenderPreview>(new RenderPreview(asset.Id), queue: "processing");
services.AddRecurringJob<CleanupJob>("cleanup", "0 3 * * *");
```

One instance needs nothing else. With several API instances, a `redis` connection string turns on the SignalR backplane and shares presence and cache invalidation between them. The Aspire app host adds the Redis container on its own; `UseRedis(...)` points at an existing server, `WithoutRedis()` leaves it out. If Redis stops answering, the services log a warning and carry on with what they know locally.

**Status:** a campaign whose worker shuts down mid-run stays in `Sending` and is not resumed, and the presence query reads one Redis hash per instance, so it is meant for a handful of instances.

- [Realtime and notifications](../modules/realtime.md): topics, active role, presence, notification endpoints
- [Chat](../modules/chat.md) and [comments, tags and ratings](../modules/social.md)
- [Mailing](../modules/mailing.md): templates, campaigns, mail providers
- [Background jobs](../modules/background-jobs.md) and [scaling out with Redis](../hosting/redis.md)
