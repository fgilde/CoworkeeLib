# Kommentare, Tags und Bewertungen

`Coworkee.Social` hängt Kommentare, Tags und Sternebewertungen an jede Entität, angesprochen über Entitätstyp und Id, ähnlich dem CMS-Kit anderer Frameworks. Entitätstypen werden ausdrücklich freigeschaltet: Eine App nennt die Typen, die den jeweiligen Baustein bekommen.

```csharp
[DependsOn(typeof(CoworkeeSocialModule))]
public sealed class MyAppProductsModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context) =>
        context.Services.AddCoworkeeSocial(social => social
            .Comments<Product>("Products", id => $"/products/{id}")
            .Tags<Product>("Products")
            .Ratings<Product>("Products"));
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

Moderieren schließt Erstellen ein, Erstellen schließt Ansehen ein.

## Kommentare

Kommentare bilden über `ParentId` einen Thread. Wer einen Kommentar löscht, löscht seine Antworten mit. Jede Änderung geht über das Realtime-Topic `comments:{entityType}:{entityId}` (`SocialTopics.Comments`) hinaus, offene Threads laden also live nach. Ist `Coworkee.Notifications` in der App, bekommen der Ersteller einer auditierten Entität und alle bisherigen Kommentatoren eine Benachrichtigung mit dem Pfad aus der Registrierung.

## Tags

Jeder Entitätstyp hat pro Organisation eine eigene Tag-Sammlung. Namen werden ohne Beachtung der Groß-/Kleinschreibung verglichen; Speichern ersetzt die Tags einer Entität. `GET /api/v1/tags/{entityType}/entities?tag=red` liefert die Ids der getaggten Entitäten, für Filter.

## Bewertungen

Eine Bewertung mit 1 bis 5 Sternen pro Benutzer und Entität; eine zweite ersetzt die erste. Die Zusammenfassung enthält Durchschnitt, Anzahl und die eigene Bewertung.

| Endpunkt | |
|---|---|
| `GET/POST /api/v1/comments/{entityType}/{entityId}` | der Thread / Kommentar mit `{ "text", "parentId" }` |
| `PUT/DELETE /api/v1/comments/{id}` | bearbeiten / löschen |
| `GET /api/v1/tags/{entityType}` | Tag-Wolke mit Anzahl |
| `DELETE /api/v1/tags/{entityType}?tag=` | Tag aus der Sammlung entfernen |
| `GET/PUT /api/v1/tags/{entityType}/{entityId}` | Tags einer Entität / ersetzen mit `{ "tags": [...] }` |
| `GET/PUT/DELETE /api/v1/ratings/{entityType}/{entityId}` | Zusammenfassung / bewerten mit `{ "stars" }` / zurücknehmen (`?userId=` für Moderatoren) |

## Datenbank

Das Modul bringt die Tabellen `cw.Comments`, `cw.Tags`, `cw.EntityTags` und `cw.Ratings` mit; nach dem Hinzufügen in der App eine Migration erzeugen.

## Im Client

```razor
<CoworkeeRating EntityType="Products" EntityId="product.Id" />
<CoworkeeTags EntityType="Products" EntityId="product.Id" />
<CoworkeeComments EntityType="Products" EntityId="product.Id" />
```

Die Komponenten fragen den Server, was der Benutzer darf, und blenden Eingaben sonst aus. `CoworkeeTagCloud` zeigt die Tag-Sammlung und macht aus dem gewählten Tag einen OData-Filter für eine Datentabelle:

```razor
<CoworkeeTagCloud EntityType="Products" @bind-Filter="_tagFilter" @bind-Filter:after="() => _table.ReloadAsync()" />
<CoworkeeDataTable @ref="_table" T="Product" EntitySet="Products" Filter="@_tagFilter" />
```
