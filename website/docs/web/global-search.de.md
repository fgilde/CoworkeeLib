# Globale Suche

Eine Suche über alles, was ein Benutzer lesen darf: Benutzer, Rollen, Gruppen, Dateien, Berichte, Chats und die eigenen Datensätze der App. Die Befehlspalette (**Strg+K**) zeigt die Treffer Gruppe für Gruppe, sobald sie da sind, `/search?q=` listet alle mit einem Filter pro Typ, und das MCP-Tool `global_search` gibt sie an KI-Clients.

## Was eine Suche findet

| Eingabe | Findet |
|---|---|
| `max` | zuerst Werte, die mit *max* beginnen, dann Werte, die es enthalten |
| `max power` | Datensätze, bei denen jedes Wort in einem der durchsuchten Felder steht |
| `"Max Power"` | nur Werte, die genau dem Text entsprechen |
| `users:max` oder `type:users max` | nur in einem Provider; `user:` und `prod:` gehen auch (der Anfang des Provider-Namens) |
| eine GUID | den Datensatz mit dieser Id |

Jeder Treffer bekommt nach denselben Regeln eine Bewertung von 0 bis 1 (gleich 1, Anfang etwa 0,85, Wortanfang etwa 0,65, Teil etwa 0,45, alle Wörter 0,3), deshalb lassen sich Treffer verschiedener Provider gemeinsam sortieren. `Weight` eines Providers skaliert seine Bewertungen.

## Datensätze deiner OData-Sets

Ein mit `AddODataEntity` registriertes Set wird mit einer Zeile im selben Modul durchsuchbar:

```csharp title="MyApp.Catalog/CatalogModule.cs"
context.Services.AddODataEntity<Product>("Products", CatalogPermissions.Products.View);
context.Services.AddGlobalSearch<Product>(s => s
    .Fields(p => p.Name, p => p.Barcode, p => p.Description)
    .Subtitle(p => p.Barcode)
    .Thumbnail(p => p.ImageDataUrl)
    .Page("/catalog/products")
    .Icon("ViewCarousel"));
```

Die Suche sieht genau das, was die OData-Liste des Sets liefert: ihre Berechtigung, die Organisation, [Zeilenregeln](odata.md#regeln-pro-zeile) und das weiche Löschen. Wer eine versteckte Eigenschaft durchsucht, bekommt beim Start einen Fehler. Gesucht wird in zwei Runden: Werte, die mit dem Text beginnen (oder der Datensatz einer eingegebenen Id), kommen sofort, Werte, die jedes Wort enthalten, folgen.

Felder dürfen über eine Navigation lesen (`t => t.Customer!.Name`): sie werden in der Abfrage gelesen und folgen den [Navigationsregeln](odata.md#navigationen-folgen-ihrem-ziel), ein ausgeblendetes Ziel passt also weder noch erscheint es. `Title`, `Subtitle`, `Url` und `Thumbnail` laufen auf der geladenen Zeile, die keine Navigationen hat; Werte anderer Zeilen gehören in Felder.

`$search` auf der OData-Liste des Sets (und ihren Exporten) sucht in denselben Feldern, außer die Entität nennt mit `ISearchable` eigene; ein Feld, das das Suchfeld der Tabelle finden soll, gehört hierher.

| Option | Standard |
|---|---|
| `Fields(...)` | Pflicht; das erste ist der Titel |
| `Title`, `Subtitle` | erstes Feld, keiner |
| `Url(p => ...)` | `Page`, gesucht nach dem Titel |
| `Page("/list")` | die Listenseite; ein Treffer öffnet sie gesucht nach dem Datensatz |
| `Thumbnail(p => ...)` | keins; Data-URLs über 32 KB bleiben weg |
| `Name`, `Group` | das Entity-Set in Kleinbuchstaben, das Entity-Set |
| `Icon("Person")` | ein Material-Icon-Name (oder SVG-Markup) |
| `Weight(0.8)` | 1 |

## Provider der Bibliothek

| Provider | Findet | Berechtigung |
|---|---|---|
| `users` | Name, Benutzername, E-Mail; öffnet den Benutzer | `Identity.Users.View` |
| `roles`, `groups` | Name und Beschreibung | `Identity.Roles.View`, `Identity.Groups.View` |
| `files` | Dateinamen; öffnet die Dateiseite mit der Vorschau | `Files.View` |
| `reports` | Berichte, die der Benutzer sieht (eigene, Organisation, eigene Rollen) | `Reports.View` |
| `chats` | die eigenen Unterhaltungen, deren Nachrichten die Wörter enthalten | `Chat.Use` |

Seiten und Aktionen bleiben in der Palette selbst (sie sind im Browser bekannt).

## Eigene Provider

Alles andere, zum Beispiel ein fremdes System, implementiert `IGlobalSearchProvider`. Provider sind Singletons; jede Suche bekommt einen eigenen Scope, so laufen die Provider nebeneinander.

```csharp
public sealed class TicketSearch : IGlobalSearchProvider
{
    public string Name => "tickets";
    public string Group => "Tickets";
    public string? Icon => "ConfirmationNumber";
    public string? Permission => "Tickets.View";

    public async IAsyncEnumerable<GlobalSearchHit> SearchAsync(GlobalSearchQuery query, GlobalSearchContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        var tickets = context.Services.GetRequiredService<ITicketService>();
        foreach (var ticket in await tickets.FindAsync(query.Text, context.Limit, ct))
        {
            var match = SearchMatch.Best(query, ticket.Title, ticket.Number);
            yield return new GlobalSearchHit(Name, Group, ticket.Title)
            {
                Url = $"/tickets/{ticket.Id}", Id = ticket.Id.ToString(), Score = match?.Score ?? 0.3, Highlights = match?.Highlights ?? [],
            };
        }
    }
}

services.AddGlobalSearchProvider<TicketSearch>();
```

## Suchindex (Elasticsearch)

Mit dem [Suchmodul](../modules/search.md) wird ein Index-Alias zum Provider. Der Index kennt die Regeln der Datenbank nicht: Jedes Dokument trägt seine Organisation (`tenantId`, ein anderes Feld setzt du mit `TenantField`), und `Where` fügt die Filter hinzu, die ein Benutzer braucht.

```csharp
services.AddGlobalSearchIndex("myapp-products", s => s
    .Fields("name", "barcode").Subtitle("barcode")
    .Url("/catalog/products?id={id}")
    .Permission(CatalogPermissions.Products.View));
```

## Endpunkte

| Endpunkt | Antwort |
|---|---|
| `GET /api/v1/search?q=&types=&limit=` | alle Treffer, die besten zuerst, und wie jeder Provider geendet hat (`GlobalSearchResult`) |
| `GET /api/v1/search/stream?q=&types=&limit=` | JSON-Zeilen (`application/x-ndjson`): jeder Treffer, sobald er gefunden ist, dann eine Zeile pro Provider mit `Done`, `TimedOut` oder `Failed` |
| `GET /api/v1/search/providers` | die Provider, die der Benutzer nutzen darf |

`types` ist eine Komma-Liste von Provider-Namen, `limit` die Treffer pro Provider (`Coworkee:GlobalSearch:DefaultLimit`, Standard 5, höchstens `MaxLimit`, Standard 50). Ein Provider, der nach `Coworkee:GlobalSearch:ProviderTimeout` (3 s) noch sucht, endet als `timedOut`, ohne die anderen aufzuhalten; bricht der Browser die Anfrage ab, hören alle auf. Treffer laufen wie jede Antwort der API durch die [Response-Filter](response-filters.md), ein `ResponseFilter<GlobalSearchHit>` kann also Untertitel vor manchen Benutzern verbergen.

Der Stream geht Zeile für Zeile durch das BFF (es puffert nicht) und schickt `X-Accel-Buffering: no` für nginx; andere Proxys vor der App dürfen `application/x-ndjson` auch nicht puffern.

Das MCP-Tool `global_search` (`query`, `types`, `limit`) liefert dasselbe Ergebnis wie der JSON-Endpunkt; es liest nur.
