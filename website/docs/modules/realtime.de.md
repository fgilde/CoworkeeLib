# Realtime und Benachrichtigungen

## Was live ankommt

Jeder offene Tab folgt diesen Änderungen ohne Neuladen:

| Änderung | Was der Client tut |
|---|---|
| Eine `[Realtime]`-Entity wird angelegt, geändert oder gelöscht (auch weich gelöscht) | jede `CoworkeeDataTable` dieser Entity lädt neu; eine Detailseite mit `EntityWatch` lädt neu oder meldet, dass der Eintrag weg ist, und geht zurück |
| Rollen, Rechte einer Rolle, Gruppenmitgliedschaften oder die aktive Rolle ändern sich | der Client lädt seine Berechtigungen neu: Navigation, `PermissionView`, `AuthorizeView` und der Seitenschutz prüfen erneut, eine Seite wird zu „nicht erlaubt“ oder wieder zur Seite |
| Ein Benutzer wird deaktiviert, gesperrt, abgemeldet oder ändert sein Passwort | seine Sitzungen enden (siehe [Clients und Sitzungen](../security/clients.md)) |
| Die Einstellung zur aktiven Rolle wird umgeschaltet | jeder Client lädt seine Berechtigungen neu |
| Jemand schickt eine Chatnachricht | der Empfänger sieht eine Snackbar mit Link zur Unterhaltung, außer sie ist offen; eine offene Unterhaltung zeigt sie sofort |
| Eine Unterhaltung wird gelesen | die Ungelesen-Zähler folgen in jedem Tab (auch die Zahl am Chat-Link), der Absender sieht die Lesemarke |
| Jemand kommt online oder geht offline | ein grüner Punkt an jedem Avatar dieses Benutzers |
| Jemand ändert Namen oder Bild | jeder Avatar und jede Benutzerkarte dieses Benutzers aktualisiert sich |
| Eine Benachrichtigung kommt oder ändert sich | Glocke, Zähler und Benachrichtigungsseite folgen |
| Features, Sprachen, Themes | Feature-Schalter, Sprachmenü und Theme-Listen folgen |

## Änderungen an Entities

Markiere eine Entity, und jedes festgeschriebene Anlegen, Ändern und Löschen geht per SignalR an die Benutzer des Mandanten, die die Berechtigung haben. Ein weiches Löschen wird als Löschen gemeldet:

```csharp
[Realtime(CatalogPermissions.Brands.View)]
public sealed class Brand : AuditedEntity, IMultiTenant { }
```

Eine `CoworkeeDataTable` folgt ihrer Entity von selbst: Das Thema ist `type:` plus der Zeilentyp ohne `Dto`, eine Tabelle von `BrandDto` lädt also bei jeder Änderung an `Brand` neu, einmal je Schwall (ein Massenlöschen schickt ein Ereignis je Zeile). Setz `RealtimeTopic`, wenn der Zeilentyp anders heißt, oder einen leeren Text, um es abzuschalten:

```razor
<CoworkeeDataTable T="UserRow" EntitySet="Users" RealtimeTopic="type:User" ... />
```

Eine Detailseite beobachtet ihren Eintrag:

```razor
<EntityWatch EntityType="Brand" EntityId="Id" ReturnHref="/catalog/brands" OnChanged="LoadAsync" />
```

Alles andere abonniert ein Thema mit `RealtimeSubscription`; es folgt einem geänderten `Topic`:

```razor
<RealtimeSubscription Topic="type:Brand" OnEvent="_ => LoadAsync()" />
```

`ExecuteUpdate` und `ExecuteDelete` umgehen den Change Tracker, dafür wird nichts veröffentlicht. Sag es den Clients nach so einer Massenänderung selbst:

```csharp
await publisher.PublishAsync(tenantId, RealtimeTopics.Type(nameof(Brand)), RealtimeEventTypes.EntityChanged,
    new EntityChangedPayload(nameof(Brand), "*", EntityChangedPayload.Deleted, []), ct);
```

Private Daten brauchen mehr Sorgfalt: Entities, die nur manche Benutzer sehen dürfen, sollten das Typ-Thema nicht nutzen. Veröffentliche dann eigene Themen:

```csharp
await publisher.PublishAsync(tenantId, $"folder:{folder.Id}", "AssetAdded", new { asset.Id }, ct);
```

Mit mehreren API-Instanzen setzt du den Connection-String `redis` für die SignalR-Backplane. Der AppHost macht das für jeden Dienst, der `Coworkee.Realtime` referenziert. Dieselbe Verbindung trägt die Sitzungsänderungen zwischen den Instanzen (siehe [Clients und Sitzungen](../security/clients.md)): Jede Instanz beendet die Hub-Verbindungen einer beendeten Sitzung, egal welche Instanz sie hält.

## Berechtigungen

Das Speichern von Rollen, Rechten von Rollen oder Benutzern, Gruppenmitgliedschaften, Gruppenrollen oder der aktiven Rolle veröffentlicht auf `tenant:permissions`; die Einstellung unten veröffentlicht auf `global:permissions`. Der Server verwirft seine zwischengespeicherten Berechtigungen nach dem Commit, der `PermissionStore` des Clients lädt sie einmal je Schwall neu und meldet `Changed`, und der Authentifizierungsstatus lässt `AuthorizeView` und `AuthorizeRouteView` erneut prüfen. Keine Seite muss dafür etwas abonnieren.

Themen, denen ein Benutzer nicht mehr folgen darf, nimmt der Server zurück: Nach so einer Änderung fragt jede Instanz die Themen-Autorisierer erneut für die Themen ihrer Verbindungen und nimmt die Verbindungen aus den Gruppen, denen sie nicht mehr angehören dürfen. So erreichen einen Benutzer, der die Berechtigung verloren hat, keine Änderungsmeldungen dieses Entity-Typs mehr (auch nach dem Wechsel der aktiven Rolle). Der Client abonniert seine Themen erneut, wenn seine Berechtigungen gewachsen sind.

## Aktive Rolle

Ein Benutzer mit mehreren Rollen wählt in der App-Leiste eine aus („Alle Rollen“ oder eine davon). Solange eine aktiv ist, kommen seine Berechtigungen nur aus dieser Rolle: Weder seine anderen Rollen noch ihm direkt gegebene Rechte zählen. Der Server wendet das bei jeder Berechtigungsprüfung an, die API verweigert also, was die Rolle nicht erlaubt; sie gibt nie mehr als die Rollen des Benutzers. Die Wahl wird beim Benutzer gespeichert (als Benutzer-Claim) und folgt ihm auf jedes Gerät; eine ihm entzogene Rolle fällt auf alle Rollen zurück.

Die Systemeinstellung **In einer Rolle handeln** (`Account.ActiveRole`, unter Anmeldesicherheit) schaltet das ein und aus; sie ist standardmäßig an. Ohne das Account-Modul ist die aktive Rolle immer erlaubt.

Ist eine Seite nicht erlaubt, listet `NotAllowed` die anderen Rollen des Benutzers, die die Berechtigungen der Seite geben, und wechselt mit einem Klick zu einer davon; die Seite erscheint dann von selbst. Der Router der App zeigt es:

```razor
<AuthorizeRouteView RouteData="routeData" DefaultLayout="typeof(CoworkeeLayout)">
    <NotAuthorized>
        <NotAllowed PageType="routeData.PageType" />
    </NotAuthorized>
</AuthorizeRouteView>
```

| Endpunkt | |
|---|---|
| `GET /api/v1/identity/roles/me?permission=` | eigene Rollen, die aktive, ob jede die Berechtigung gibt |
| `PUT /api/v1/identity/roles/me/active` | `{ "roleId": null }` handelt wieder mit allen Rollen |

## Anwesenheit

Ein Benutzer mit mindestens einer Hub-Verbindung ist online, egal wie viele Tabs er offen hat; nach dem Schließen der letzten geht er nach ein paar Sekunden offline, sodass ein Neuladen nicht flackert. Änderungen gehen als `presence.changed` auf `tenant:presence`, `GET /api/v1/realtime/presence` listet, wer aus der Organisation online ist, und `UserAvatar` zeigt einen grünen Punkt (`ShowPresence="false"` blendet ihn aus). Mit mehreren API-Instanzen zählt ein Benutzer je Instanz.

## Chat

Eine Nachricht geht als `chat.message` an die Themen `user:{id}` aller Mitglieder ihres Chats, das Lesen als `chat.read`, das Schreiben als `chat.typing`, Namens- und Mitgliederänderungen als `chat.conversation` ([Chat](chat.md#live-ereignisse)). Der `ChatNotifier` der App-Leiste zeigt eine Snackbar mit Absender und Textanfang, deren Aktion den Chat als Mini-Chat öffnet; er bleibt still, solange dieser Chat auf der Chatseite (`/chat?c={id}`) oder im Dock offen ist. Er setzt außerdem die Zahl der ungelesenen Nachrichten (`GET /api/v1/chat/unread-count`) an den Chat-Link der Navigation und hält sie in jedem Tab aktuell.

Jede Seite kann so eine Zahl an ihrem Navigationslink zeigen: `NavigationBadges.Set("/orders", 3)`.

## Benachrichtigungen

```csharp
await notifier.NotifyAsync([reviewerId], "review.requested", "Review requested", $"{asset.Name} waits for you", link: $"/assets/{asset.Id}", ct);
```

Lokalisierbare Benachrichtigungen nehmen die englischen Texte als Schlüssel mit Platzhaltern `{0}` und die Argumente getrennt; jeder Leser sieht sie in seiner Sprache (die Texte gehören in die Übersetzungen der App), auch in der Zusammenfassungsmail, die die `Localization.Culture` des Lesers nimmt (seine eigene, sonst die seiner Organisation, sonst die des Systems):

```csharp
await notifier.NotifyLocalizedAsync(adminIds, "account.registration", "New registration", "{0} ({1}) waits for activation.", [name, email], $"/admin/users/{id}", ct);
```

Benutzer sehen sie in der Glocke der App-Leiste und auf der Benachrichtigungsseite, live über SignalR. Eine tägliche Zusammenfassung verschickt ungelesene per Mail (`Coworkee:Notifications:DigestCron`), mit Links auf `PublicAppUrl`.

Die Glocke zeigt die Zahl der ungelesenen und die letzten zehn; eine neu eintreffende Benachrichtigung erscheint als Snackbar mit Link darauf. Ein Klick markiert sie als gelesen und öffnet ihren Link (nur Seiten der App). Jede lässt sich wieder als ungelesen markieren und löschen; die Glocke markiert alle als gelesen, die Seite `/notifications` (auch ein Reiter von `/profile`) blättert durch alle oder nur die ungelesenen und löscht alle. Jede Änderung erreicht sofort alle offenen Tabs des Benutzers: das `NotificationCenter` des Clients hält die Zahl der ungelesenen und meldet `Changed` einmal pro Schwall, auch nach „Alle als gelesen markieren“.

| Endpunkt | |
|---|---|
| `GET /api/v1/notifications?unreadOnly=&type=&page=&pageSize=` | neueste zuerst |
| `GET /api/v1/notifications/unread-count` | |
| `POST /api/v1/notifications/{id}/read`, `/{id}/unread` | |
| `POST /api/v1/notifications/read-all` | |
| `GET /api/v1/notifications?category=&done=&search=` | Filter des Eingangs: Kategorie, offen (`done=false`) oder erledigt, Text in Titel oder Inhalt |
| `GET /api/v1/notifications/counts` | offene, ungelesene und offene pro Kategorie |
| `POST /api/v1/notifications/{id}/done`, `/{id}/undone` | erledigt markiert auch als gelesen |
| `GET /api/v1/notifications/preferences`, `PUT .../preferences/{type}` | die Wahl des Benutzers pro registriertem Typ |
| `GET /api/v1/notifications/follows`, `GET/PUT/DELETE .../follows/{entityType}/{entityId}` | verfolgte Entitäten, Status, verfolgen, nicht mehr verfolgen |
| `DELETE /api/v1/notifications/{id}`, `DELETE /api/v1/notifications` | eine, alle |

### Einstellungen, Verfolgen und verzögerte Mails

Apps registrieren die Benachrichtigungstypen, die Benutzer selbst wählen, und die Entitätstypen, denen sie folgen dürfen:

```csharp
services.AddNotificationTypes(n => n
    .Add("shipment.eta", "Shipment", "Estimated arrival changed", NotificationScope.Followed, inApp: true, email: true, emailDelay: TimeSpan.FromMinutes(1))
    .Add("quotation.new", "Quotation", "New quotation", NotificationScope.All)
    .Followable<Shipment>("Shipments", id => $"/shipments/{id}"));
```

Ein Typ hat eine Kategorie (Filter und Zahlen des Eingangs), einen Namen (Übersetzungsschlüssel) und Vorgaben: den Umfang (`Off`, `All`, `Followed`) und die Kanäle (in der App, per E-Mail). Jeder Benutzer überschreibt sie pro Typ im Reiter *Einstellungen* von `/notifications` (`NotificationPreference`, Tabelle `cw.NotificationPreferences`). Verfolgen ist pro Entitätstyp freizuschalten: Mit `Followable<TEntity>` muss der Benutzer die Zeile über das OData-Set der Entität sehen (403 ohne die Berechtigung des Sets, 404 für eine verborgene, fremde oder geratene Id).

!!! warning "`Followable(string)` prüft nichts"
    Mit `Followable("Type")` dürfen Benutzer jeder Id folgen, und niemand prüft, ob sie sie sehen dürfen. Benachrichtigungen über solche Entitäten erreichen Verfolger deshalb nur über ein `Audience`, darauf beschränkt; ohne Publikum bekommen ihre Verfolger nichts. Datensätze, die manche Benutzer nicht sehen dürfen, gehören zu `Followable<TEntity>`.
 `CoworkeeFollowButton` (`EntityType`, `EntityId`, `Title`) verfolgt und beendet das auf einer Detailseite; der Reiter *Gefolgt* listet, was der Benutzer verfolgt. `NotificationFollow` (`cw.NotificationFollows`) ist öffentlich, Apps filtern ihre Listen also danach (nur verfolgte, eine Spalte „verfolgt“).

`INotifier.PublishAsync(NotificationMessage)` sendet an drei Arten von Empfängern:

```csharp
await notifier.PublishAsync(new NotificationMessage("shipment.eta", "Estimated arrival of {0} changed", "New: {1}")
{
    Arguments = [shipment.Number, eta.ToString("d")],
    Link = $"/shipments/{shipment.Id}",
    EntityType = "Shipments", EntityId = shipment.Id,  // ihre Verfolger
    Audience = usersWhoMaySeeIt,                        // alle mit der Einstellung All
    Recipients = [shipment.OwnerId],                    // direkt adressiert
}, ct);
```

Direkt Adressierte bekommen sie, wenn ihr Umfang nicht Off ist; Verfolger bei All oder Followed, wenn sie die Entität noch sehen: auf das Publikum beschränkt, wenn eines angegeben ist, sonst wird jeder Verfolger als er selbst gegen das Set der `Followable<TEntity>`-Registrierung geprüft (Berechtigung, Zeilenfilter, Gruppen); das Publikum bei All. Der Auslöser bekommt sie nur, wenn er adressiert ist. Pro Empfänger entscheiden die Kanäle: in der App eine Benachrichtigung, per E-Mail die Mail `Notifications.Notification` in der Sprache des Lesers. Ein Typ mit `emailDelay` lässt die Mail in `cw.PendingNotificationMails` warten; eine weitere Benachrichtigung desselben Benutzers, Typs und derselben Entität ersetzt sie vorher und wartet neu, ein Schwall von Änderungen endet also in einer Mail mit dem neuesten Text. Der Job `coworkee-notification-mails` verschickt fällige Mails jede Minute. Eine wartende Mail ist pro Benutzer, Typ und Entität eindeutig und wird sofort in einem eigenen Kontext gespeichert, parallele Benachrichtigungen oder der Job lassen den sendenden Befehl also nie scheitern (ein Befehl, der danach scheitert, kann seine Mail wartend zurücklassen); der Job nimmt eine Mail nur so, wie er sie gelesen hat, eine inzwischen ersetzte wartet neu. `NotifyAsync` und `NotifyLocalizedAsync` sind `PublishAsync` mit `Recipients`; nicht registrierte Typen erreichen sie wie bisher nur in der App.

Der Eingang listet standardmäßig die offenen Benachrichtigungen; *Erledigt* nimmt eine heraus (und markiert sie als gelesen), die Reiter zeigen offene, erledigte oder alle, die Chips filtern nach Kategorie mit ihren offenen Zahlen, die Suche sucht in Titel und Inhalt.
