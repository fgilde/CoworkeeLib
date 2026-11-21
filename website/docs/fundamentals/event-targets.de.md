# Events und Ziele

Wer etwas bekommt, das passiert, ein Toast, eine Benachrichtigung, eine Mail oder ein Realtime-Event, ist ein Wert: ein `EventTarget`. Dasselbe Ziel gilt im [Notifier](../modules/realtime.md#benachrichtigungen) und für Realtime-Events und wird in der aktuellen Organisation aufgelöst.

```csharp
await notifier.SendAsync(new NotificationRequest(EventTarget.Permission(OrderPermissions.View).Except(EventTarget.Current), "Order {0} shipped")
{
    Arguments = [order.Number],
    Link = $"/orders/{order.Id}",
    Delivery = Delivery.Stored | Delivery.Toast,
    Severity = NotificationSeverity.Success,
    Mail = MailDelivery.WhenOffline,
}, ct);

await notifier.ToastAsync(EventTarget.All, "Maintenance at 18:00", NotificationSeverity.Warning, ct);
```

## Ziele

| Ziel | Wer |
|---|---|
| `EventTarget.All` | jeder Benutzer der Organisation |
| `EventTarget.Global` | jeder Benutzer jeder Organisation: nur Realtime und Toasts (Systemmeldungen); gespeicherte Benachrichtigungen lehnen es ab |
| `EventTarget.Current` | die aktuelle Sitzung (alle Tabs dieser Anmeldung) bei Realtime und Toasts, sonst und in `Except` der aktuelle Benutzer |
| `EventTarget.Users(id1, id2)` | diese Benutzer |
| `EventTarget.Permission("Orders.View")` | die Benutzer mit der Berechtigung |
| `EventTarget.Role("Dispatcher")` | die Benutzer mit der Rolle, direkt oder über eine Gruppe |
| `EventTarget.Group(groupId)` | die Mitglieder der Gruppe und ihrer Untergruppen |
| `EventTarget.Users(u => ...)` | die Benutzer, auf die ein Prädikat passt (unten) |
| `EventTarget.Custom<TResolver>(args)` | die Benutzer, die ein `IAudienceResolver` der App nennt |
| `EventTarget.Users(selection)` | die Benutzer einer `UserSelection` (Filter der Benutzer-Menge, hinzugefügt, ausgelassen) mit den Rechten des aktuellen Benutzers ([unten](#benutzerauswahl)) |
| `a \| b`, `a.Or(b)` | die Benutzer beider |
| `a.Except(b)` | die Benutzer von `a` ohne die von `b`, z. B. `.Except(EventTarget.Current)`, um den Handelnden auszulassen |

### Prädikate

Ein Prädikat über `EventUser` (`Id`, `UserName`, `Email`, `FirstName`, `LastName`) läuft in der Datenbank als **eine Abfrage**, mit vier Markern, die zu Unterabfragen über Rollen, Freigaben und Gruppen der Bibliothek werden:

```csharp
EventTarget.Users(u => u.InGroup(salesId) && u.HasPermission(OrderPermissions.Approve) && !u.IsInRole("Trainee"))
EventTarget.Users(u => u.Email!.EndsWith("@partner.example") && u.IsOnline())
```

| Marker | Bedeutung |
|---|---|
| `HasPermission(name)` | wie `IPermissionChecker`: eine Rolle (direkt oder über eine Gruppe) mit der Berechtigung oder einer, die sie einschließt, die Admin-Rolle, eine Freigabe des Benutzers oder einer seiner Gruppen (die Freigabe einer Gruppe reicht nicht in ihre Untergruppen). Berechnete Berechtigungen, Freigaben auf einzelnen Ressourcen und die gewählte aktive Rolle zählen nicht |
| `IsInRole(name)` | hat die Rolle, direkt oder über eine seiner Gruppen |
| `InGroup(id)` | Mitglied der Gruppe oder einer ihrer Untergruppen |
| `IsOnline()` | hat gerade eine Realtime-Verbindung |

Die Argumente der Marker sind Werte (Konstanten, Variablen), nie etwas vom Benutzer. Was die Datenbank nicht ausführen kann, schlägt sofort mit einer Meldung fehl, die es nennt; nichts wird Benutzer für Benutzer im Speicher ausgewertet.

### Zielgruppen der App

Eine Zielgruppe, die die Bibliothek nicht kennen kann, etwa die Beteiligten einer Sendung, ist ein `IAudienceResolver`; registriert oder nicht (ein nicht registrierter wird mit seinen Abhängigkeiten erzeugt):

```csharp
public sealed class ShipmentAudience(AppDbContext db) : IAudienceResolver
{
    public async Task<IReadOnlyCollection<Guid>> ResolveAsync(object? args, CancellationToken ct) =>
        await db.ShipmentParties.Where(p => p.ShipmentId == (Guid)args!).Select(p => p.UserId).ToListAsync(ct);
}

await notifier.SendAsync(new NotificationRequest(EventTarget.Custom<ShipmentAudience>(shipment.Id), "Shipment departed"), ct);
```

## Benutzerauswahl

Eine `UserSelection` ist das, was jemand in einer Benutzertabelle gewählt hat: alle, auf die der Filter passt (`AllMatching`, `Filter` ist ein OData-`$filter` der Menge Users, null für alle), dazu `Included`, ohne `Excluded` (je höchstens 1.000 IDs, `UserSelection.MaxIds`). Sie ist ein schlichter, serialisierbarer Record: speichern, senden, später auflösen. Eine große Auswahl reist als Filter, nie als Tausende IDs durch den Browser.

```csharp
var selection = new UserSelection("Groups/any(g: g/Id eq 6a1f...)", Excluded: [bobId]);
await notifier.SendAsync(new NotificationRequest(EventTarget.Users(selection), "Inventur beginnt am Montag"), ct);

UserSelection.Of(aliceId, carlId);   // nur diese Benutzer, wenn der Aufrufer sie sehen darf
```

Der Server löst sie mit den Rechten des **aktuellen Benutzers** auf, so wie die Menge Users sie zeigt: `Identity.Users.View`, die Organisation, die Zeilenfilter der Menge und die Gruppenauswahl; einen Filter, den er nicht ausführen darf (eine verborgene Eigenschaft, ein unbekannter Name), lehnt er ab. Vom Browser hinzugefügte IDs zählen nur, wenn der Aufrufer diese Benutzer sieht. Als Ziel erreicht die Auswahl die aktiven Benutzer der Organisation und folgt den Regeln der Ziele: der Handelnde bleibt außen vor (die Auswahl nennt ihn nicht per ID), Einstellungen gelten. Ohne das Recht, Benutzer zu sehen, wirft `EventTarget.Users(selection)`; `IUserSelectionResolver.ResolveAsync(selection)` liefert eigenem Code die IDs oder den Fehler (`Forbidden`, `Validation`).

`POST /api/v1/identity/users/selection/count` (`Identity.Users.View`, Body: die Auswahl) antwortet `{ "users": 12, "active": 11 }` (`ICoworkeeApi.CountUserSelectionAsync`).

### Benutzer wählen in Blazor

`CoworkeeUserSelection` (`Coworkee.Client.Blazor.Components.Users`) ist eine Datentabelle über die Menge Users mit ihren Facetten, der Suche und den Spaltenfiltern: der Schalter *Alle, auf die der Filter passt* nimmt jeden Treffer, das Kästchen einer Zeile lässt einen Benutzer aus oder nimmt ihn dazu, *Benutzer hinzufügen* nimmt jeden per Name dazu. **Rollen** schränkt auf die Mitglieder von Rollen ein, direkt vergeben oder über eine Gruppe mit der Rolle (wie Berechtigungen Rollen zählen); **Gruppen** auf die Mitglieder von Gruppen und ihren Untergruppen. Beides wird angeboten, wo der Benutzer Rollen bzw. Gruppen lesen darf. Darunter steht die Zahl des Servers, live: eine Änderung von Benutzern lädt die Tabelle neu und zählt erneut.

```razor
<CoworkeeUserSelection @bind-Value="_recipients" CountChanged="c => _count = c" Filter="IsActive eq true" />

@code {
    private UserSelection _recipients = new();
    private UserSelectionCountDto? _count;
}
```

Die gewählten Rollen und Gruppen werden OData-Filter der Auswahl: `Roles/any(r: r/RoleId in (...)) or Groups/any(m: m/GroupId in (...))` und `Groups/any(m: m/GroupId in (...))` (die Menge Users hat dafür die Navigation `Roles`, die direkten Rollen eines Benutzers). `Filter` schränkt die Tabelle ein und wird Teil der Auswahl; `PageSize` setzt die Zeilen je Seite. Der Wert geht so, wie er ist, an den Server, z. B. in einer eigenen Anfrage mit `EventTarget.Users(request.Recipients)`.

## Benachrichtigungen aus der Administration senden

Die Seite *Benachrichtigung senden* (Administration → Kommunikation, `/admin/notifications/send`, Seitenschlüssel `send-notification`, Berechtigung `Notifications.Send`) sendet eine Benachrichtigung an eine Benutzerauswahl: Titel, Text, optionale Argumente (eins je Zeile: Titel und Text werden englische Texte mit `{0}`-Platzhaltern, die jeder Leser in seiner Sprache sieht), Link (ein Pfad der App oder eine http(s)-Adresse), Schweregrad, Eingang und/oder Toast, Mail (wie jeder Empfänger es gewählt hat, nie, immer, wenn offline) und den Typ (der allgemeine oder ein registrierter Typ). Die Empfänger zu wählen braucht zusätzlich `Identity.Users.View`; die System-Admin-Rolle hat beides. Die Seite sendet über `INotifier.SendAsync`, also gelten die Regeln oben: der Absender bleibt außen vor, Einstellungen gelten, ein Toast erreicht sofort, wer online ist.

Dasselbe aus Code oder einem anderen Client: `POST /api/v1/notifications/send` mit einer `SendNotificationRequest` (`ICoworkeeApi.SendNotificationAsync`):

```json
{ "recipients": { "filter": "contains(Email,'@acme.test')", "allMatching": true, "excluded": ["..."] },
  "title": "Maintenance at {0}", "body": "Please save your work.", "arguments": ["18:00"],
  "link": "/status", "severity": "Warning", "stored": true, "toast": true, "mail": "WhenOffline", "type": null }
```

## Wie Ziele aufgelöst werden

`IEventTargetResolver` (Coworkee.Identity implementiert ihn) löst in der aktuellen Organisation auf; ein Ziel schränkt nur ein:

- **Realtime und Toasts** (`TopicsAsync`): `All` geht einmal an das Thema der Organisation `tenant:events`, `Global` an `global:events`, `Current` an das Thema der Sitzung (`session:{sid}`, ohne Sitzung, etwa in einem Job, das eigene Thema des Benutzers). Jedes andere Ziel fragt zuerst die [Anwesenheit](../modules/realtime.md#anwesenheit) nach den angemeldeten Benutzern der Organisation und führt das Ziel dann nur für sie aus; jeder bekommt das Event auf seinem Thema `user:{id}`. Eine große Organisation kostet, was ihre angemeldeten Benutzer kosten, abgemeldete kosten nichts.
- **Gespeicherte Benachrichtigungen und Mails** (`UsersAsync`): aktive Benutzer der Organisation in einer Abfrage. Benutzer-IDs (auch mit `Current` und `Except` von IDs) brauchen keine Datenbank und gelten wie angegeben, wie bei `NotifyAsync` schon immer.

Benutzer anderer Organisationen und inaktive Benutzer werden nie aufgelöst, auch nicht aus einem `IAudienceResolver`. Die Themen behalten ihre Prüfung beim Abonnieren: Ein Client folgt nur seinem eigenen Benutzer- und Sitzungsthema, `tenant:events` seiner Organisation und `global:events`.

## Realtime-Events für ein Ziel

`RealtimeEvents` veröffentlicht an ein Ziel statt an ein Thema. In einer Unit of Work mit offenen Änderungen geht das Event nach dem Commit hinaus (bei einem Rollback nicht), sonst sofort:

```csharp
internal sealed class ApproveOrderHandler(AppDbContext db, RealtimeEvents events) : IHandler<ApproveOrder, Result>
{
    public async Task<Result> HandleAsync(ApproveOrder command, CancellationToken ct)
    {
        var order = await db.Orders.SingleAsync(o => o.Id == command.Id, ct);
        order.Approve();
        await events.PublishAsync(EventTarget.Permission(OrderPermissions.View).Except(EventTarget.Current), "orders.approved", new { order.Id }, ct);
        return Result.Success();
    }
}
```

Clients bekommen es auf den Themen, denen sie ohnehin folgen (`user:{id}`, `tenant:events`, `global:events`, `session:{sid}`); eine Blazor-Seite abonniert eines davon mit `RealtimeClient.SubscribeAsync` und prüft den `Type`. Wie jede Realtime-Nutzlast geht es an jeden, den das Ziel nennt: IDs und was sich geändert hat senden, keine Daten, die einige davon nicht sehen dürfen.

## Typisierte Events

Ein Event-Typ bekommt einen stabilen Namen und wird an ein Ziel veröffentlicht; der Name übersteht Umbenennen oder Verschieben des Typs (ohne Attribut ist es der kurze Typname, nie der volle CLR-Name):

```csharp
[EventName("orders.approved")]
public sealed record OrderApproved(Guid OrderId, string Number);   // in den Contracts, mit dem Client geteilt

await events.PublishAsync(EventTarget.Permission(OrderPermissions.View), new OrderApproved(order.Id, order.Number), ct);
```

In Blazor abonniert `RealtimeClient.On<T>`, deserialisiert und behält die gezählten Abonnements, das Neuverbinden und Wiederabonnieren von `SubscribeAsync`:

```csharp
// ein Event des Servers: auf jedem Thema, auf dem ein Ziel diesen Benutzer erreicht (sein eigenes, tenant:events, global:events, seine Sitzung)
await using var approved = await Realtime.On<OrderApproved>(e => InvokeAsync(() => Snackbar.Add(L["Order {0} approved", e.Number])));

// Änderungen an Entities: OrderDto folgt type:Order (der Name ohne "Dto"), mit wer und wann; skipOwn lässt eigene Änderungen aus
await using var changes = await Realtime.On<EntityChanged<OrderDto>>(c => InvokeAsync(ReloadAsync), skipOwn: true);
await using var one = await Realtime.On<EntityChanged<OrderDto>>(c => c.IsDeleted ? GoBackAsync() : ReloadAsync(), topic: RealtimeTopics.Entity("Order", id));
```

```razor
<RealtimeEvent T="EntityChanged<OrderDto>" OnEvent="ReloadAsync" SkipOwn="true" />
<RealtimeEvent T="OrderApproved" OnEvent="ShowApproved" />
```

`EntityChanged<TDto>` trägt `EntityType`, `EntityId`, `Action`, `ChangedProperties`, `Actor` und `At`, nie Werte: Der Client lädt neu, was er sehen darf. Auch `ToastPayload` hat seinen Namen (`On<ToastPayload>` nutzt der `ToastNotifier` der Shell). In TypeScript macht `onEvent<T>(realtime, 'orders.approved', targetedTopics(userId, sessionId), handler, { skipActor: userId })` dasselbe.

## Wer, wann, warum

Jeder `RealtimeEnvelope` trägt Metadaten, die der Publisher ohne Abfrage füllt: `Id` (eindeutig pro Nachricht), `At` (wann gesendet), `Actor` (`ActorInfo(UserId, Name, ImpersonatorId)`; null für das System) und `CorrelationId` (der Trace der Anfrage). Eine Komponente kann eigene Änderungen übergehen oder sagen, wer eine gemacht hat:

```csharp
_subscription = await Realtime.SubscribeAsync(RealtimeTopics.Type("Order"), envelope =>
    envelope.Actor?.UserId == _me ? Task.CompletedTask : InvokeAsync(ReloadAsync));
```

Ältere Clients übergehen die neuen Felder; `@coworkee/client` kennt sie (`RealtimeEnvelope.actor`, `Toast`).

## Toasts im Client

`Delivery.Toast` sendet `notification.toast` (`ToastPayload`: Titel, Text, Schweregrad, Link, Argumente, Typ) an das Ziel, ohne Zeile und ohne Mail. `CoworkeeAppShell` (und damit `CoworkeeLayout`) zeigt es angemeldeten Benutzern als Snackbar, übersetzt wie Benachrichtigungen (mit `Arguments`, auch leer, sind Titel und Text englische Texte mit Platzhaltern `{0}`) und mit dem Link als Aktion *Öffnen*. `options.ShowToasts = false` schaltet das ab; die App setzt dann `<ToastNotifier />` selbst oder behandelt `ToastPayload.Event` selbst.
