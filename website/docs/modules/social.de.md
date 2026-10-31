# Kommentare, Tags, Bewertungen und Reaktionen

`Coworkee.Social` hängt Kommentare, Tags, Sternebewertungen und Emoji-Reaktionen an jede Entität, angesprochen über Entitätstyp und Id, ähnlich dem CMS-Kit anderer Frameworks. Entitätstypen werden ausdrücklich freigeschaltet: Eine App nennt die Typen, die den jeweiligen Baustein bekommen.

```csharp
[DependsOn(typeof(CoworkeeSocialModule))]
public sealed class MyAppProductsModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context) =>
        context.Services.AddCoworkeeSocial(social => social
            .Comments<Product>("Products", id => $"/products/{id}")
            .Tags<Product>("Products")
            .Ratings<Product>("Products")
            .Reactions<Product>("Products")
            .CommentReactions());
}
```

Mit der Entitätsklasse gibt es die Social-Daten einer Zeile nur für Benutzer, die die Zeile sehen: hat der Typ ein [OData-Set](../web/odata.md), so wie dieses Set sie zeigt (Berechtigung des Sets, Organisation, Soft Delete und jeder Zeilenfilter `IODataEntityFilter<T>`); ohne Set jede Zeile der eigenen Organisation. Alle anderen bekommen 404 auf Kommentare, Tags, Bewertungen und Reaktionen und dürfen den Echtzeit-Topics nicht folgen; Tag-Zähler und `tags/{entityType}/entities` zählen nur sichtbare Zeilen. Ohne die Entitätsklasse (`Comments("Products")`) wird jede Id angenommen. `AddCoworkeeSocial` darf von mehreren Modulen aufgerufen werden.

Ein optionaler Hook entscheidet zusätzlich, ob der Benutzer die Daten einer Entität sehen (`Write == false`) oder ändern darf, z. B. für einen Entitätstyp ohne OData-Set:

```csharp
social.Authorize(async (services, access, ct) =>
    access.EntityId is not { } id || await services.GetRequiredService<IProductAccess>().CanSeeAsync(id, ct));
```

## Datenbank

Das Modul bringt die Tabellen `cw.Comments` (mit `Status` und `Properties`), `cw.Tags`, `cw.EntityTags`, `cw.Ratings` und `cw.Reactions` mit; erzeuge in der App nach dem Hinzufügen und nach Updates, die sie ändern, eine Migration.

## Kommentare

Kommentare bilden über `ParentId` einen Thread. Wer einen Kommentar löscht, löscht seine Antworten mit. Jede Änderung geht über das Realtime-Topic `comments:{entityType}:{entityId}` (`SocialTopics.Comments`) hinaus, offene Threads laden also live nach. Ist `Coworkee.Notifications` in der App, bekommen der Ersteller einer auditierten Entität und alle bisherigen Kommentatoren eine Benachrichtigung mit dem Pfad aus der Registrierung.

### Status, App-Werte und Prüfungen

Ein Kommentar hat einen `Status`: `None`, `Open` (eine Frage oder Anfrage, die auf Antwort wartet) oder `Closed`. Er wird beim Schreiben gesetzt (`CommentRequest.Status`) und von Inhabern von `Social.Comments.SetStatus` geändert, mit `PUT /api/v1/comments/{id}/status` (`SetCommentStatusCommand`) oder beim Bearbeiten. `Properties` hält App-Werte eines Kommentars, etwa eine Referenznummer (höchstens 20, Namen bis 50 und Werte bis 500 Zeichen); ein Bearbeiten ohne sie behält sie.

Apps prüfen Kommentare vor jeder Änderung mit `ValidateComments`; die Prüfung sieht Aktion, Entität, Text, Status und Werte, und ihr Fehler ist die Antwort (es wird nichts geschrieben):

```csharp
services.AddCoworkeeSocial(social => social
    .Comments<Quotation>("Quotations", id => $"/quotations/{id}")
    .ValidateComments(async (services, comment, ct) =>
        await services.GetRequiredService<AppDbContext>().Quotations.AnyAsync(q => q.Id == comment.EntityId && q.Status == QuoteStatus.Booked, ct)
            ? Error.Conflict("quotations.booked", "Comments cannot be added or changed on a booked quotation.")
            : null));
```

Jedes Hinzufügen, Bearbeiten, Ändern des Status und Löschen löst das Domain-Event `CommentChanged(CommentId, EntityType, EntityId, Status, Action)` aus. Es läuft über die Outbox, ein `IDomainEventHandler<CommentChanged>` läuft also nach dem Commit, als der Benutzer, der die Änderung gemacht hat, und liest den Thread, wie er jetzt ist: Ein Angebot geht auf *Anfrage*, solange einer seiner Kommentare Open ist, sonst zurück.

## Tags

Jeder Entitätstyp hat pro Organisation eine eigene Tag-Sammlung. Namen werden ohne Beachtung der Groß-/Kleinschreibung verglichen; Speichern ersetzt die Tags einer Entität. `GET /api/v1/tags/{entityType}/entities?tag=red` liefert die Ids der getaggten Entitäten, für Filter.

## Bewertungen

Eine Bewertung mit 1 bis 5 Sternen pro Benutzer und Entität; eine zweite ersetzt die erste. Die Zusammenfassung enthält Durchschnitt, Anzahl und die eigene Bewertung.

## Reaktionen

Jeder Benutzer kann mehrere Emojis an eine Entität setzen, jedes einmal; dasselbe Emoji noch einmal nimmt es zurück (höchstens 20 pro Benutzer und Entität). Reaktionen listen die Benutzer pro Emoji, die erste Reaktion zuerst, und ändern sich live über das Topic `reactions:{entityType}:{entityId}` (`SocialTopics.Reactions`).

`CommentReactions()` schaltet sie für die Kommentare jedes Entitätstyps mit Kommentaren frei, Entitätstyp `Social.Comment` (`SocialReactions.Comment`) mit der Id des Kommentars. Wer die Kommentare einer Entität sehen oder schreiben darf, darf dort Reaktionen sehen oder setzen, zusätzlich mit den Reaktionsberechtigungen. Der Thread bringt die Reaktionen seiner Kommentare mit, sie ändern sich live über das Kommentar-Topic des Threads und verschwinden, wenn ihr Kommentar gelöscht wird.

## Im Client

```razor
<CoworkeeRating EntityType="Products" EntityId="product.Id" />
<CoworkeeTags EntityType="Products" EntityId="product.Id" />
<CoworkeeReactions EntityType="Products" EntityId="product.Id" />
<CoworkeeComments EntityType="Products" EntityId="product.Id" />
```

Die Komponenten fragen den Server, was der Benutzer darf, und blenden Eingaben sonst aus. `CoworkeeReactions` zeigt einen Chip pro Emoji mit Anzahl (die eigenen gefüllt), beim Überfahren, wer reagiert hat, und eine Auswahl mit `SocialReactions.Defaults` (👍 ❤️ 😂 🎉 😮 😢; `Emojis` setzt andere). Mit `CommentReactions()` zeigen die Kommentare dieselbe Zeile unter jedem Kommentar. `CoworkeeComments` zeigt den Status jedes Kommentars, bietet Inhabern von `Social.Comments.SetStatus` ein Status-Menü und gibt neuen Kommentaren den Status `NewStatus` (Standard `None`). `ISocialApi` (`SetCommentStatusAsync`, `AddCommentAsync(entityType, entityId, CommentRequest)`, Reaktionen, Tags, Bewertungen) ruft dieselben Endpunkte aus eigenen Komponenten auf. `CoworkeeTagCloud` zeigt die Tag-Sammlung und macht aus dem gewählten Tag einen OData-Filter für eine Datentabelle:

```razor
<CoworkeeTagCloud EntityType="Products" @bind-Filter="_tagFilter" @bind-Filter:after="() => _table.ReloadAsync()" />
<CoworkeeDataTable @ref="_table" T="Product" EntitySet="Products" Filter="@_tagFilter" />
```

## Berechtigungen

| Baustein | Ansehen | Erstellen | Moderieren |
|---|---|---|---|
| Kommentare | `Social.Comments.View` | schreiben, eigene bearbeiten und löschen | jeden Kommentar löschen |
| Tags | `Social.Tags.View` | Entitäten taggen, neue Tags kommen in die Sammlung | Tag aus der Sammlung entfernen |
| Bewertungen | `Social.Ratings.View` | bewerten, eigene Bewertung zurücknehmen | jede Bewertung entfernen |
| Reaktionen | `Social.Reactions.View` | reagieren, eigene Reaktion zurücknehmen | – |

Moderieren schließt Erstellen ein, Erstellen schließt Ansehen ein.

`Social.Comments.SetStatus` (schließt Ansehen ein) ändert den Status jedes Kommentars. Autoren geben einen Status nur beim Schreiben eines Kommentars an (wie in einem Anfrage-Thread); jede spätere Änderung, auch das Wiederöffnen eines geschlossenen Kommentars, braucht `Social.Comments.SetStatus`, beim Bearbeiten wie mit `PUT /api/v1/comments/{id}/status`.

## Grenzen

| | |
|---|---|
| Kommentar | Text 4.000 Zeichen; `Properties` höchstens 20, Namen 50 und Werte 500 Zeichen |
| Tags | 50 je Entität, Namen 50 Zeichen |
| Reaktionen | 20 je Benutzer und Entität, ein Emoji 16 Zeichen |
| Entitätstyp | Name 100 Zeichen |

## Personenbezogene Daten

Der Export personenbezogener Daten hat einen Abschnitt `social` mit den Kommentaren, Bewertungen und Reaktionen des Benutzers; das Löschen eines Benutzers entfernt sie.

## Endpunkte

| Endpunkt | |
|---|---|
| `GET/POST /api/v1/comments/{entityType}/{entityId}` | der Thread / Kommentar mit `{ "text", "parentId", "status", "properties" }` |
| `PUT/DELETE /api/v1/comments/{id}` | den eigenen Kommentar bearbeiten mit `{ "text", "status", "properties" }` / löschen |
| `PUT /api/v1/comments/{id}/status` | den Status setzen mit `{ "status" }` (`Social.Comments.SetStatus`) |
| `GET /api/v1/tags/{entityType}` | Tag-Wolke mit Anzahl |
| `DELETE /api/v1/tags/{entityType}?tag=` | Tag aus der Sammlung entfernen |
| `GET/PUT /api/v1/tags/{entityType}/{entityId}` | Tags einer Entität / ersetzen mit `{ "tags": [...] }` |
| `GET/PUT/DELETE /api/v1/ratings/{entityType}/{entityId}` | Zusammenfassung / bewerten mit `{ "stars" }` / zurücknehmen (`?userId=` für Moderatoren) |
| `GET/POST /api/v1/reactions/{entityType}/{entityId}` | Reaktionen mit den Reagierenden / eigene umschalten mit `{ "emoji" }` |
