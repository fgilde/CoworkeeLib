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
| Eine Unterhaltung wird gelesen | die Ungelesen-Zähler folgen in jedem Tab, der Absender sieht die Lesemarke |
| Jemand ändert Namen oder Bild | jeder Avatar und jede Benutzerkarte dieses Benutzers aktualisiert sich |
| Eine Benachrichtigung kommt oder ändert sich | Glocke, Zähler und Benachrichtigungsseite folgen |
| Features, Sprachen, Themes | Feature-Schalter, Sprachmenü und Theme-Listen folgen |

## Änderungen an Entities

Markieren Sie eine Entity, und jedes festgeschriebene Anlegen, Ändern und Löschen geht per SignalR an die Benutzer des Mandanten, die die Berechtigung haben. Ein weiches Löschen wird als Löschen gemeldet:

```csharp
[Realtime(CatalogPermissions.Brands.View)]
public sealed class Brand : AuditedEntity, IMultiTenant { }
```

Eine `CoworkeeDataTable` folgt ihrer Entity von selbst: Das Thema ist `type:` plus der Zeilentyp ohne `Dto`, eine Tabelle von `BrandDto` lädt also bei jeder Änderung an `Brand` neu, einmal je Schwall (ein Massenlöschen schickt ein Ereignis je Zeile). Setzen Sie `RealtimeTopic`, wenn der Zeilentyp anders heißt, oder einen leeren Text, um es abzuschalten:

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

`ExecuteUpdate` und `ExecuteDelete` umgehen den Change Tracker, dafür wird nichts veröffentlicht. Sagen Sie es den Clients nach so einer Massenänderung selbst:

```csharp
await publisher.PublishAsync(tenantId, RealtimeTopics.Type(nameof(Brand)), RealtimeEventTypes.EntityChanged,
    new EntityChangedPayload(nameof(Brand), "*", EntityChangedPayload.Deleted, []), ct);
```

Private Daten brauchen mehr Sorgfalt: Entities, die nur manche Benutzer sehen dürfen, sollten das Typ-Thema nicht nutzen. Veröffentlichen Sie dann eigene Themen:

```csharp
await publisher.PublishAsync(tenantId, $"folder:{folder.Id}", "AssetAdded", new { asset.Id }, ct);
```

Mit mehreren API-Instanzen setzen Sie den Connection-String `redis` für die SignalR-Backplane. Der AppHost macht das für jeden Dienst, der `Coworkee.Realtime` referenziert. Dieselbe Verbindung trägt die Sitzungsänderungen zwischen den Instanzen (siehe [Clients und Sitzungen](../security/clients.md)): Jede Instanz beendet die Hub-Verbindungen einer beendeten Sitzung, egal welche Instanz sie hält.

## Berechtigungen

Das Speichern von Rollen, Rechten von Rollen oder Benutzern, Gruppenmitgliedschaften, Gruppenrollen oder der aktiven Rolle veröffentlicht auf `tenant:permissions`; die Einstellung unten veröffentlicht auf `global:permissions`. Der Server verwirft seine zwischengespeicherten Berechtigungen nach dem Commit, der `PermissionStore` des Clients lädt sie einmal je Schwall neu und meldet `Changed`, und der Authentifizierungsstatus lässt `AuthorizeView` und `AuthorizeRouteView` erneut prüfen. Keine Seite muss dafür etwas abonnieren.

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

## Chat

Eine Nachricht geht als `chat.message` an die Themen `user:{id}` beider Personen, das Lesen einer Unterhaltung als `chat.read`. Der `ChatNotifier` der App-Leiste zeigt eine Snackbar mit Absender, Textanfang und Link auf `/chat?with={absender}`; die Chatseite hält die offene Unterhaltung in dieser Adresse, sodass sie still bleibt, solange Sie darin sind.

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
| `DELETE /api/v1/notifications/{id}`, `DELETE /api/v1/notifications` | eine, alle |
