---
title: "Echtzeit und Zusammenarbeit in Blazor-Apps"
description: "SignalR-Echtzeit für ASP.NET Core und Blazor: Tabellen und Detailseiten live, Präsenz, Benachrichtigungen, Chat, Kommentare, Hintergrundjobs und Redis-Backplane."
---

# Realtime und Zusammenarbeit

Eine Coworkee-App hält pro Tab eine SignalR-Verbindung, und das meiste, was andere tun, kommt darüber an. Eine Tabelle lädt neu, wenn sich ihre Entity ändert. Eine Detailseite merkt, dass ihr Datensatz gelöscht wurde, und springt zurück zur Liste. Nimmt ein Admin eine Rolle weg, schrumpft die Navigation, während der Benutzer noch draufschaut.

Meist reicht dafür ein Attribut an der Entity, in der Seite ist nichts zu tun.

## Entity-Änderungen in der Oberfläche

```csharp
[Realtime(CatalogPermissions.Brands.View)]
public sealed class Brand : AuditedEntity, IMultiTenant { }
```

Jedes gespeicherte Anlegen, Ändern und Löschen von `Brand` geht jetzt an die Benutzer des Mandanten, die `Brands.View` haben (weiches Löschen zählt als Löschen). Eine `CoworkeeDataTable` von `BrandDto` folgt dem von selbst und lädt einmal pro Schub neu, 200 gelöschte Zeilen bedeuten also nicht 200 Neuladevorgänge. Detailseiten und alles andere abonnieren ausdrücklich:

```razor
<EntityWatch EntityType="Brand" EntityId="Id" ReturnHref="/catalog/brands" OnChanged="LoadAsync" />
<RealtimeSubscription Topic="type:Brand" OnEvent="_ => LoadAsync()" />
```

Zwei Dinge bleiben bei dir. Datensätze, die nur manche sehen dürfen, schickst du über eigene Topics mit `IRealtimePublisher.PublishAsync`. Und `ExecuteUpdate`/`ExecuteDelete` laufen am Change Tracker vorbei, dafür wird nichts veröffentlicht, solange du es nicht selbst tust.

Berechtigungsänderungen nehmen denselben Weg. Wird eine Rolle, ein Recht oder eine Gruppenmitgliedschaft gespeichert, lädt der Client seine Rechte neu, `AuthorizeView` und der Seitenschutz prüfen erneut, und der Server nimmt den Benutzer aus Topics, denen er nicht mehr folgen darf. Wer mehrere Rollen hat, wählt in der App-Leiste eine aus und handelt nur in ihr; die Komponente `NotAllowed` bietet den Wechsel in eine Rolle an, mit der die Seite aufgeht.

## Präsenz, Benachrichtigungen und Chat

Wer mindestens eine offene Verbindung hat, ist online und bekommt einen grünen Punkt am `UserAvatar`. Nach dem letzten geschlossenen Tab dauert es ein paar Sekunden bis „offline“, damit ein Neuladen nicht flackert.

Benachrichtigungen landen in der Glocke der App-Leiste und unter `/notifications`, live in jedem Tab, die ungelesenen zusätzlich einmal am Tag als Mail:

```csharp
await notifier.NotifyAsync([reviewerId], "review.requested", "Review requested",
    $"{asset.Name} waits for you", link: $"/assets/{asset.Id}", ct);
```

`NotifyLocalizedAsync` macht dasselbe mit englischen Texten als Schlüssel, jeder Leser bekommt dann seine Sprache. Registrierte Benachrichtigungstypen (`AddNotificationTypes`) bringen Kategorien und Einstellungen pro Benutzer, in der App und per E-Mail, das Verfolgen von Datensätzen (`Followable<T>`, `CoworkeeFollowButton`), `INotifier.PublishAsync` für Empfänger, Verfolgende und ein Publikum sowie Mails pro Benachrichtigung mit Verzögerung, Bündelung und einer Mail-Vorlage pro Typ ([Einstellungen, Verfolgen und verzögerte Mails](../modules/realtime.md#einstellungen-verfolgen-und-verzogerte-mails)).

`Coworkee.Chat` bringt Direkt- und Gruppenchats innerhalb einer Organisation: formatierte Nachrichten, `@`-Erwähnungen von Personen und `#`-Erwähnungen von Datensätzen, Antworten, Reaktionen, Dateien und Bilder, Lesemarken und eine Tipp-Anzeige. Eine neue Nachricht erscheint als Snackbar, außer der Chat ist schon offen, und Mini-Chats docken unten rechts an ([Chat](../modules/chat.md)).

## Kommentare, Tags, Bewertungen und Reaktionen

`Coworkee.Social` hängt Kommentare mit Antworten, Tags, Bewertungen von 1 bis 5 Sternen und Emoji-Reaktionen an Entities, die du dafür freigibst:

```csharp
context.Services.AddCoworkeeSocial(social => social
    .Comments<Product>("Products", id => $"/products/{id}")
    .Tags<Product>("Products"));
```

Jeder Baustein hat eigene Rechte zum Ansehen, Anlegen und Moderieren. `CoworkeeComments`, `CoworkeeTags` und `CoworkeeRating` bekommen Entity-Typ und Id und blenden aus, was der Benutzer nicht darf; Kommentar-Threads laden live nach. Kommentare können einen Status tragen (offen, geschlossen), den `Social.Comments.SetStatus` ändert, und `CoworkeeReactions` zeigt die Emoji-Leiste ([Status, App-Werte und Prüfungen](../modules/social.md#status-app-werte-und-prufungen), [Reaktionen](../modules/social.md#reaktionen)).

## Mails, Jobs und die Redis-Backplane

Mails sind Scriban-Vorlagen mit Vorgaben je Sprache, die Admins überschreiben können. Eigene Vorlagen gehen auch als Kampagne an intelligente Benutzerlisten. Verschickt wird über eine Warteschlange, ein Hintergrundjob wiederholt Fehlschläge.

Jobs laufen auf Hangfire, und zwar als der Benutzer, der sie eingereiht hat, Mandantenfilter und Audit verhalten sich also wie in einer Anfrage:

```csharp
jobs.Enqueue<RenderPreviewJob, RenderPreview>(new RenderPreview(asset.Id), queue: "processing");
services.AddRecurringJob<CleanupJob>("cleanup", "0 3 * * *");
```

Eine Instanz braucht nichts weiter. Bei mehreren API-Instanzen schaltet ein Connection String `redis` die SignalR-Backplane ein und teilt Präsenz und Cache-Invalidierung zwischen ihnen. Den Redis-Container legt der Aspire-App-Host selbst an; `UseRedis(...)` zeigt auf einen vorhandenen Server, `WithoutRedis()` lässt ihn weg. Antwortet Redis nicht mehr, schreiben die Dienste eine Warnung ins Log und arbeiten mit dem lokalen Stand weiter.

**Stand:** Fährt der Worker mitten in einer Kampagne herunter, bleibt sie auf `Sending` stehen und wird nicht fortgesetzt. Die Präsenzabfrage liest einen Redis-Hash pro Instanz und ist damit für eine Handvoll Instanzen gedacht.

- [Realtime und Benachrichtigungen](../modules/realtime.md): Topics, aktive Rolle, Präsenz, Endpunkte für Benachrichtigungen
- [Chat](../modules/chat.md) und [Kommentare, Tags, Bewertungen und Reaktionen](../modules/social.md)
- [Mailing](../modules/mailing.md): Vorlagen, Kampagnen, Mail-Anbieter
- [Hintergrundjobs](../modules/background-jobs.md) und [Skalierung mit Redis](../hosting/redis.md)
