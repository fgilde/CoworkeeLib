# Realtime und Benachrichtigungen

## Änderungen an Entities

Markieren Sie eine Entity, und jedes festgeschriebene Anlegen, Ändern und Löschen geht per SignalR an die Benutzer des Mandanten, die die Berechtigung haben:

```csharp
[Realtime(CatalogPermissions.Brands.View)]
public sealed class Brand : AuditedEntity, IMultiTenant { }
```

```razor
<RealtimeSubscription Topic="type:Brand" OnEvent="_ => _table.ReloadAsync()" />
```

Private Daten brauchen mehr Sorgfalt: Entities, die nur manche Benutzer sehen dürfen, sollten das Typ-Thema nicht nutzen. Veröffentlichen Sie dann eigene Themen:

```csharp
await publisher.PublishAsync(tenantId, $"folder:{folder.Id}", "AssetAdded", new { asset.Id }, ct);
```

Mit mehreren API-Instanzen setzen Sie den Connection-String `redis` für die SignalR-Backplane. Der AppHost macht das für jeden Dienst, der `Coworkee.Realtime` referenziert. Dieselbe Verbindung trägt die Sitzungsänderungen zwischen den Instanzen (siehe [Clients und Sitzungen](../security/clients.md)): Jede Instanz beendet die Hub-Verbindungen einer beendeten Sitzung, egal welche Instanz sie hält.

## Benachrichtigungen

```csharp
await notifier.NotifyAsync([reviewerId], "review.requested", "Review requested", $"{asset.Name} waits for you", link: $"/assets/{asset.Id}", ct);
```

Lokalisierbare Benachrichtigungen nehmen die englischen Texte als Schlüssel mit Platzhaltern `{0}` und die Argumente getrennt; jeder Leser sieht sie in seiner Sprache (die Texte gehören in die Übersetzungen der App), auch in der Zusammenfassungsmail, die die `Localization.Culture` des Lesers nimmt (seine eigene, sonst die seiner Organisation, sonst die des Systems):

```csharp
await notifier.NotifyLocalizedAsync(adminIds, "account.registration", "New registration", "{0} ({1}) waits for activation.", [name, email], $"/admin/users/{id}", ct);
```

Benutzer sehen sie in der Glocke der App-Leiste und auf der Benachrichtigungsseite, live über SignalR. Eine tägliche Zusammenfassung verschickt ungelesene per Mail (`Coworkee:Notifications:DigestCron`), mit Links auf `PublicAppUrl`.
