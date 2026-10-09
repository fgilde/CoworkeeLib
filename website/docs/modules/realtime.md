# Realtime and notifications

## Entity changes

Mark an entity, and every committed insert, update and delete is pushed over SignalR to the users of the tenant who hold the permission:

```csharp
[Realtime(CatalogPermissions.Brands.View)]
public sealed class Brand : AuditedEntity, IMultiTenant { }
```

```razor
<RealtimeSubscription Topic="type:Brand" OnEvent="_ => _table.ReloadAsync()" />
```

Private data needs more care: entities only some users may see should not use the type topic. Publish your own topics instead:

```csharp
await publisher.PublishAsync(tenantId, $"folder:{folder.Id}", "AssetAdded", new { asset.Id }, ct);
```

With several API instances, set the connection string `redis` for the SignalR backplane. The app host does that for every service that references `Coworkee.Realtime`. The same connection carries the session changes between the instances (see [Clients and sessions](../security/clients.md)): each instance ends the hub connections of a session that was ended, whichever instance holds them.

## Notifications

```csharp
await notifier.NotifyAsync([reviewerId], "review.requested", "Review requested", $"{asset.Name} waits for you", link: $"/assets/{asset.Id}", ct);
```

Localizable notifications take the English texts as keys with `{0}` placeholders and the arguments apart; every reader sees them in his language (the texts belong into the app's translations), also in the digest mail, which takes the reader's `Localization.Culture` (his own, else his organisation's, else the system's):

```csharp
await notifier.NotifyLocalizedAsync(adminIds, "account.registration", "New registration", "{0} ({1}) waits for activation.", [name, email], $"/admin/users/{id}", ct);
```

Users see them in the bell of the app bar and on the notifications page, live through SignalR. A daily digest mails unread ones (`Coworkee:Notifications:DigestCron`), with links to `PublicAppUrl`.
