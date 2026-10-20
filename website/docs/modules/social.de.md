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

Mit der Entitätsklasse wird die Id über den `DbContext` geprüft, Mandantenfilter gelten also; ohne sie (`Comments("Products")`) wird jede Id angenommen. `AddCoworkeeSocial` darf von mehreren Modulen aufgerufen werden.

Ein optionaler Hook entscheidet zusätzlich zu den Berechtigungen, ob der Benutzer die Daten einer Entität sehen (`Write == false`) oder ändern darf, z. B. nur Produkte, die er sehen darf:

```csharp
social.Authorize(async (services, access, ct) =>
    access.EntityId is not { } id || await services.GetRequiredService<IProductAccess>().CanSeeAsync(id, ct));
```

## Berechtigungen

| Baustein | Ansehen | Erstellen | Moderieren |
|---|---|---|---|
| Kommentare | `Social.Comments.View` | schreiben, eigene bearbeiten und löschen | jeden Kommentar löschen |
| Tags | `Social.Tags.View` | Entitäten taggen, neue Tags kommen in die Sammlung | Tag aus der Sammlung entfernen |
| Bewertungen | `Social.Ratings.View` | bewerten, eigene Bewertung zurücknehmen | jede Bewertung entfernen |
| Reaktionen | `Social.Reactions.View` | reagieren, eigene Reaktion zurücknehmen | – |

Moderieren schließt Erstellen ein, Erstellen schließt Ansehen ein.

## Kommentare

Kommentare bilden über `ParentId` einen Thread. Wer einen Kommentar löscht, löscht seine Antworten mit. Jede Änderung geht über das Realtime-Topic `comments:{entityType}:{entityId}` (`SocialTopics.Comments`) hinaus, offene Threads laden also live nach. Ist `Coworkee.Notifications` in der App, bekommen der Ersteller einer auditierten Entität und alle bisherigen Kommentatoren eine Benachrichtigung mit dem Pfad aus der Registrierung.

## Tags

Jeder Entitätstyp hat pro Organisation eine eigene Tag-Sammlung. Namen werden ohne Beachtung der Groß-/Kleinschreibung verglichen; Speichern ersetzt die Tags einer Entität. `GET /api/v1/tags/{entityType}/entities?tag=red` liefert die Ids der getaggten Entitäten, für Filter.

## Bewertungen

Eine Bewertung mit 1 bis 5 Sternen pro Benutzer und Entität; eine zweite ersetzt die erste. Die Zusammenfassung enthält Durchschnitt, Anzahl und die eigene Bewertung.

## Reaktionen

Jeder Benutzer kann mehrere Emojis an eine Entität setzen, jedes einmal; dasselbe Emoji noch einmal nimmt es zurück (höchstens 20 pro Benutzer und Entität). Reaktionen listen die Benutzer pro Emoji, die erste Reaktion zuerst, und ändern sich live über das Topic `reactions:{entityType}:{entityId}` (`SocialTopics.Reactions`).

`CommentReactions()` schaltet sie für die Kommentare jedes Entitätstyps mit Kommentaren frei, Entitätstyp `Social.Comment` (`SocialReactions.Comment`) mit der Id des Kommentars. Wer die Kommentare einer Entität sehen oder schreiben darf, darf dort Reaktionen sehen oder setzen, zusätzlich mit den Reaktionsberechtigungen. Der Thread bringt die Reaktionen seiner Kommentare mit, sie ändern sich live über das Kommentar-Topic des Threads und verschwinden, wenn ihr Kommentar gelöscht wird.

| Endpunkt | |
|---|---|
| `GET/POST /api/v1/comments/{entityType}/{entityId}` | der Thread / Kommentar mit `{ "text", "parentId" }` |
| `PUT/DELETE /api/v1/comments/{id}` | bearbeiten / löschen |
| `GET /api/v1/tags/{entityType}` | Tag-Wolke mit Anzahl |
| `DELETE /api/v1/tags/{entityType}?tag=` | Tag aus der Sammlung entfernen |
| `GET/PUT /api/v1/tags/{entityType}/{entityId}` | Tags einer Entität / ersetzen mit `{ "tags": [...] }` |
| `GET/PUT/DELETE /api/v1/ratings/{entityType}/{entityId}` | Zusammenfassung / bewerten mit `{ "stars" }` / zurücknehmen (`?userId=` für Moderatoren) |
| `GET/POST /api/v1/reactions/{entityType}/{entityId}` | Reaktionen mit den Reagierenden / eigene umschalten mit `{ "emoji" }` |

## Datenbank

Das Modul bringt die Tabellen `cw.Comments`, `cw.Tags`, `cw.EntityTags`, `cw.Ratings` und `cw.Reactions` mit; nach dem Hinzufügen in der App eine Migration erzeugen.

## Im Client

```razor
<CoworkeeRating EntityType="Products" EntityId="product.Id" />
<CoworkeeTags EntityType="Products" EntityId="product.Id" />
<CoworkeeReactions EntityType="Products" EntityId="product.Id" />
<CoworkeeComments EntityType="Products" EntityId="product.Id" />
```

Die Komponenten fragen den Server, was der Benutzer darf, und blenden Eingaben sonst aus. `CoworkeeReactions` zeigt einen Chip pro Emoji mit Anzahl (die eigenen gefüllt), beim Überfahren, wer reagiert hat, und eine Auswahl mit `SocialReactions.Defaults` (👍 ❤️ 😂 🎉 😮 😢; `Emojis` setzt andere). Mit `CommentReactions()` zeigen die Kommentare dieselbe Zeile unter jedem Kommentar. `CoworkeeTagCloud` zeigt die Tag-Sammlung und macht aus dem gewählten Tag einen OData-Filter für eine Datentabelle:

```razor
<CoworkeeTagCloud EntityType="Products" @bind-Filter="_tagFilter" @bind-Filter:after="() => _table.ReloadAsync()" />
<CoworkeeDataTable @ref="_table" T="Product" EntitySet="Products" Filter="@_tagFilter" />
```
