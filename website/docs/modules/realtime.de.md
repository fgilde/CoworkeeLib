# Realtime und Benachrichtigungen

`Coworkee.Realtime` (`CoworkeeRealtimeModule`) betreibt einen SignalR-Hub, der jedem offenen Tab Änderungen meldet; `Coworkee.Notifications` (`CoworkeeNotificationsModule`, hängt von Realtime und [Mailing](mailing.md) ab) bringt die Benachrichtigungen der Benutzer, ihren Eingang, Einstellungen, Mails und Push. Apps aus der Vorlage haben beide (Vorlagenoption `notifications`, standardmäßig an):

```csharp
[DependsOn(typeof(CoworkeeNotificationsModule))]
public sealed class MyAppDatabaseModule : CoworkeeModule;
```

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

Jede Änderung geht an zwei Themen: `type:Brand` (jede Zeile des Typs) und `entity:Brand:{id}` (diese Zeile). Eine `CoworkeeDataTable` folgt ihrer Entity von selbst: Das Thema ist `type:` plus der Zeilentyp ohne `Dto`, eine Tabelle von `BrandDto` lädt also bei jeder Änderung an `Brand` neu, einmal je Schwall (ein Massenlöschen schickt ein Ereignis je Zeile). Setz `RealtimeTopic`, wenn der Zeilentyp anders heißt, oder einen leeren Text, um es abzuschalten; eine Tabelle über eine `DataSource` folgt nichts, solange `RealtimeTopic` nicht gesetzt ist:

```razor
<CoworkeeDataTable T="UserRow" EntitySet="Users" RealtimeTopic="type:User" ... />
```

Eine Detailseite beobachtet ihren Eintrag (Thema `entity:{EntityType}:{EntityId}`):

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

Eine Entity kann auch eigene Themen nennen: `IHasRealtimeTopics.RealtimeTopics` an der Entity oder ein `IRealtimeTopicMapper`-Dienst für Entities, die nicht dir gehören (`TopicsFor(entity)`). Ihre Änderungen gehen ebenfalls als `EntityChanged` an diese Themen, auch ohne `[Realtime]`; die Benachrichtigungen eines Benutzers etwa gehen an `user:{id}`.

Mit mehreren API-Instanzen setzt du den Connection-String `redis` für die SignalR-Backplane. Der AppHost macht das für jeden Dienst, der `Coworkee.Realtime` referenziert. Dieselbe Verbindung trägt die Sitzungsänderungen zwischen den Instanzen (siehe [Clients und Sitzungen](../security/clients.md)): Jede Instanz beendet die Hub-Verbindungen einer beendeten Sitzung, egal welche Instanz sie hält.

## Themen

Clients verbinden sich mit dem Hub unter `/hubs/realtime` (angemeldet; die Verbindung endet, wenn ihr Token abläuft) und rufen `Subscribe(topic)` und `Unsubscribe(topic)`; jede Nachricht kommt über `OnEvent` als `RealtimeEnvelope(Topic, Type, Payload)` an. Der Server lässt eine Verbindung einem Thema nur beitreten, wenn ein `IRealtimeTopicAuthorizer`, der dessen Präfix behandelt, zustimmt:

| Thema | Wer ihm folgen darf | Was dort ankommt |
|---|---|---|
| `type:{Entity}`, `entity:{Entity}:{id}` | wer die Berechtigung des `[Realtime]`-Attributs der Entity hat; bei `entity:` auch, wer sie auf dieser Ressource hat (`[Realtime(permission, ResourceType = ...)]`) | `EntityChanged` |
| `user:{id}` | dieser Benutzer | seine Benachrichtigungen, Chat, `SessionRevoked` |
| `session:{sid}` (`RealtimeTopics.Session`) | Verbindungen, deren Token diese `sid` trägt | `ImpersonationEnded` (`ImpersonationEndedPayload`), wenn ein *Anmelden als* endet, siehe [Identity](../features/identity.md#als-anderer-benutzer-anmelden) |
| `tenant:...` | jeder angemeldete Benutzer der Organisation | z. B. `tenant:permissions`, `tenant:presence` |
| `global:...` | jeder angemeldete Benutzer | z. B. `global:permissions` |

Andere Präfixe werden abgelehnt, außer eine App registriert einen eigenen `IRealtimeTopicAuthorizer` (`Handles(topic)`, `AuthorizeAsync(topic, ct)`), wie es die Module Social, Workflows und Datensatz-Anhänge für ihre Themen tun. `IRealtimePublisher.PublishAsync(tenantId, topic, type, payload)` sendet an ein Thema; Apps können ihn dekorieren, um Nachrichten an eigene Hubs weiterzugeben. `SessionRevoked` (`SessionRevokedPayload`: Grund, `KeptSessionId`) läuft pro Sitzungsänderung auf jeder Instanz durch den Publisher, `RealtimePublisher` schickt es aber an keine Gruppe: Das Identity-Modul beendet genau die Hub-Verbindungen dieser Sitzung (siehe [Clients und Sitzungen](../security/clients.md#uberall-abmelden-und-sperren)).

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

Benutzer sehen sie in der Glocke der App-Leiste und auf der Benachrichtigungsseite, live über SignalR (Thema `user:{id}`). Eine tägliche Zusammenfassung verschickt die ungelesenen per Mail (siehe [Tägliche Zusammenfassung](#tagliche-zusammenfassung)).

Die Glocke zeigt die Zahl der ungelesenen und die letzten zehn; eine neu eintreffende Benachrichtigung erscheint als Snackbar mit Link darauf. Ein Klick markiert sie als gelesen und öffnet ihren Link (nur Seiten der App). Jede lässt sich wieder als ungelesen markieren und löschen; die Glocke markiert alle als gelesen, die Seite `/notifications` (auch ein Reiter von `/profile`) blättert durch alle oder nur die ungelesenen und löscht alle. Jede Änderung erreicht sofort alle offenen Tabs des Benutzers: das `NotificationCenter` des Clients hält die Zahl der ungelesenen und meldet `Changed` einmal pro Schwall, auch nach „Alle als gelesen markieren“.

| Endpunkt | |
|---|---|
| `GET /api/v1/notifications?unreadOnly=&type=&category=&done=&search=&page=&pageSize=` | neueste zuerst (höchstens 100 pro Seite); Filter: ungelesen, Typ, Kategorie, offen (`done=false`) oder erledigt, Text in Titel oder Inhalt |
| `GET /api/v1/notifications/unread-count` | |
| `GET /api/v1/notifications/counts` | offene, ungelesene und offene pro Kategorie |
| `POST /api/v1/notifications/{id}/read`, `/{id}/unread` | |
| `POST /api/v1/notifications/read-all` | |
| `POST /api/v1/notifications/{id}/done`, `/{id}/undone` | erledigt markiert auch als gelesen |
| `GET /api/v1/notifications/preferences`, `PUT .../preferences/{type}` | die Wahl des Benutzers pro registriertem Typ |
| `GET /api/v1/notifications/follows`, `GET/PUT/DELETE .../follows/{entityType}/{entityId}` | verfolgte Entitäten, Status, verfolgen, nicht mehr verfolgen |
| `DELETE /api/v1/notifications/{id}`, `DELETE /api/v1/notifications` | eine, alle |
| `GET /api/v1/notifications/push`, `PUT .../push`, `DELETE .../push/{id}` | Web-Push-Schlüssel und die Geräte des Benutzers, ein Gerät anmelden, eines entfernen (siehe [Push](#push)) |

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

Direkt Adressierte bekommen sie, wenn ihr Umfang nicht Off ist; Verfolger bei All oder Followed, wenn sie die Entität noch sehen: auf das Publikum beschränkt, wenn eines angegeben ist, sonst wird jeder Verfolger als er selbst gegen das Set der `Followable<TEntity>`-Registrierung geprüft (Berechtigung, Zeilenfilter, Gruppen); das Publikum bei All. Der Auslöser bekommt sie nur, wenn er adressiert ist. Pro Empfänger entscheiden die Kanäle: in der App eine Benachrichtigung, per E-Mail die Mail `Notifications.Notification` in der Sprache des Lesers, nur an eine aktive Adresse (ohne eine bleibt die Benachrichtigung in der täglichen Zusammenfassung). Ein Typ mit `emailDelay` lässt die Mail in `cw.PendingNotificationMails` warten; eine weitere Benachrichtigung desselben Benutzers, Typs und derselben Entität ersetzt sie vorher und wartet neu, ein Schwall von Änderungen endet also in einer Mail mit dem neuesten Text (eine Nachricht ohne `EntityType` und `EntityId` wird nicht zusammengefasst: jede wartet für sich). Der Job `coworkee-notification-mails` verschickt fällige Mails jede Minute. Eine wartende Mail ist pro Benutzer, Typ und Entität eindeutig und wird über die Outbox geschrieben, nachdem der sendende Befehl festgeschrieben ist: Ein Befehl, der scheitert, sendet keine Mail, und parallele Benachrichtigungen oder der Job lassen den Befehl nie scheitern (der Outbox-Handler versucht es erneut; der API-Host betreibt die Outbox mit `AddCoworkeeOutboxProcessing`); der Job nimmt eine Mail nur in der Version, die er gelesen hat (`Version` ändert sich bei jedem Ersetzen), eine inzwischen ersetzte wartet neu. Veröffentliche innerhalb des Befehls, bevor seine Unit of Work gespeichert wird (in einem Handler speichert die Pipeline), oder nach dem Commit aus einem Domain-Event-Handler: Die Benachrichtigungen in der App, sofortige Mails und die Outbox-Nachricht einer verzögerten Mail gehören zu diesem Speichern, ein scheiternder Befehl sendet also nichts, und Code, der nach seinem letzten `SaveChangesAsync` veröffentlicht, muss erneut speichern.

Die Prüfung der Verfolger kostet ohne `Audience` eine Abfrage pro Verfolger: Jeder wird als er selbst geprüft, weil Berechtigungen, Zeilenfilter und Gruppen pro Benutzer verschieden sind. Für Entitäten mit Hunderten von Verfolgern übergib ein `Audience` (die Benutzer, die die Entität sehen dürfen), das die Prüfung pro Verfolger durch eine Schnittmenge ersetzt.

`NotifyAsync` und `NotifyLocalizedAsync` sind `PublishAsync` mit `Recipients`; nicht registrierte Typen erreichen sie wie bisher nur in der App.

Der Eingang listet standardmäßig die offenen Benachrichtigungen; *Erledigt* nimmt eine heraus (und markiert sie als gelesen), die Chips *Zu erledigen*, *Erledigt* und *Alle* wechseln zwischen offenen, erledigten und allen, die Kategorie-Chips filtern nach Kategorie mit ihren offenen Zahlen, die Suche sucht in Titel und Inhalt.

### Mail-Vorlagen pro Typ und Verzögerung pro Organisation

Ein Typ kann mit einer eigenen Vorlage mailen und die Mail-Verzögerung von der Organisation des Absenders nehmen:

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

`mailTemplate` nennt eine Mail-Vorlage aus [Mailing](mailing.md) (registriert mit einem `IMailTemplateContributor` oder eine eigene Vorlage), Administratoren ändern sie also auf der Seite der Mail-Vorlagen pro Sprache und Organisation. Sie rendert `MailModel` (ein Objekt oder ein Dictionary): Seine Member sind die Namen der obersten Ebene der Vorlage, benannt wie die Member jedes Mail-Modells (`OldValue` wird `old_value`), neben `user` (`first_name`, `email`) und `notification` (`title`, `body` in der Sprache des Lesers, absoluter `link`). Die füllt die Bibliothek, `PublishAsync` weist daher ein Modell zurück, dessen oberste Ebene einen Member `user`, `notification`, eine Variable des Layouts (`subject`, `content`, `unsubscribe_url`) oder den Namen eines Scriban-Builtins (`string`, `html`, `date`, `array`, `math`, `object`, `regex`, `timespan`, ...) hat, in beliebiger Schreibweise und mit oder ohne Unterstriche (`ArgumentException`), ebenso eines, das als JSON größer als 32 KB ist (`NotificationMailModel.MaxBytes`). Das Modell wird als JSON mit der Outbox-Nachricht und der wartenden Mail gespeichert, eine verzögerte Mail rendert also das Modell der neuesten Benachrichtigung ihrer Entität; Werte kommen als Text, Zahlen, Wahrheitswerte, Listen und Objekte an, formatiere Datumswerte also vor dem Veröffentlichen. Verzögerte Mails werden in der Organisation gerendert, die sie gesendet hat (ihre Vorlagenversionen, ihr Mail-Protokoll). Ein Typ ohne Vorlage oder mit einem Namen, den keine Vorlage hat (eine Warnung geht ins Log), und eine Mail ohne Modell (etwa eine, die schon wartete, bevor ihr Typ eine Vorlage bekam) senden wie bisher die allgemeine `Notifications.Notification`; das Modell wird auch ihr übergeben, so dass die Version einer Organisation von der allgemeinen Vorlage es lesen kann. Eine Vorlage, die sich nicht rendern lässt (etwa eine bearbeitete Version, zu der das Modell nicht passt), sendet stattdessen die allgemeine Mail, und scheitert auch die, entfällt die Mail; beides wird geloggt, und keins von beiden lässt den sendenden Befehl oder die anderen Mails des Joblaufs scheitern.

Gib der Definition der Vorlage `NotificationMailModel.Sample(model)` als Beispiel: Vorschau und Testmail auf der Seite der Mail-Vorlagen zeigen dann das Beispielmodell mit einem Beispiel-`user` und einer Beispiel-`notification`.

```csharp
context.Add(new MailTemplateDefinition("Shipments.EtaChanged", "Estimated arrival changed",
    NotificationMailModel.Sample(new { shipment = new { Number = "S-1", PortName = "Hamburg" }, OldValue = "2026-05-01", NewValue = "2026-05-03" }),
    EmbeddedTemplates.Load(typeof(ShipmentMails).Assembly, "Shipments.EtaChanged")));
```

`resolveEmailDelay` läuft beim Veröffentlichen einer Benachrichtigung, mit den Diensten des Scopes des Absenders (seine Organisation, Features und Einstellungen). Sein Ergebnis ist die Verzögerung für diese Benachrichtigung; `null` behält `emailDelay`, `TimeSpan.Zero` mailt sofort. Ein Resolver, der eine Ausnahme wirft, wird geloggt, und `emailDelay` gilt; er lässt den veröffentlichenden Befehl nie scheitern. Die wartende Mail behält die Fälligkeit, die sie bekam, eine neuere Benachrichtigung derselben Entität setzt eine neue.

### Tägliche Zusammenfassung

Der Job `coworkee-notification-digest` schickt jedem Benutzer mit ungelesenen Benachrichtigungen seit seiner letzten Zusammenfassung (höchstens einen Tag zurück, höchstens 50 pro Mail) eine Mail mit der Vorlage `Notifications.Digest`, in seiner Sprache und mit Links auf `Coworkee:Notifications:PublicAppUrl` (die Web-App; ohne sie bleiben die Links relativ). Benachrichtigungen, die schon als eigene Mail hinausgingen (E-Mail-Kanal eines Typs), fehlen darin. `Coworkee:Notifications:DigestCron` legt fest, wann er läuft (Cron in UTC, Standard `0 6 * * *`); mit der Einstellung `Notifications.Digest` („Daily e-mail with unread notifications“, System und Benutzer, standardmäßig an) schalten Benutzer sie ab.

### Push

Mit einem VAPID-Schlüsselpaar (`options.UsePush()` im AppHost oder `Coworkee:Notifications:Push:PublicKey`, `PrivateKey` und `Subject`) geht jede Benachrichtigung auch als Web Push an die Geräte, die ein Benutzer im Eingang eingeschaltet hat. Einrichtung und Zustellung: [Telefone, installierbare App und Push](../ui/phones-and-pwa.md#push-benachrichtigungen).

### Aufbewahrung und personenbezogene Daten

Benachrichtigungen bleiben, bis der Benutzer sie löscht; mit dem Modul [Aufbewahrung](retention.md) löscht das Ziel `notifications` sie nach den dort eingestellten Tagen. Der Export personenbezogener Daten hat einen Abschnitt `notifications` (die Benachrichtigungen und Push-Geräte des Benutzers); das Löschen des Benutzers entfernt sie.
