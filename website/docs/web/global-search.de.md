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

Die Suche sieht genau das, was die OData-Liste des Sets liefert: ihre Berechtigung, den Mandanten, [Zeilenregeln](odata.md#regeln-pro-zeile) und das weiche Löschen. Wer eine versteckte Eigenschaft durchsucht, bekommt beim Start einen Fehler. Gesucht wird in zwei Runden: Werte, die mit dem Text beginnen (oder der Datensatz einer eingegebenen Id), kommen sofort, Werte, die jedes Wort enthalten, folgen.

Felder dürfen über eine Navigation lesen (`t => t.Customer!.Name`): sie werden in der Abfrage gelesen und folgen den [Navigationsregeln](odata.md#navigationen-folgen-ihrem-ziel), ein ausgeblendetes Ziel passt also weder noch erscheint es. `Title`, `Subtitle`, `Url` und `Thumbnail` laufen auf der geladenen Zeile, die keine Navigationen hat; Werte anderer Zeilen gehören in Felder.

`$search` auf der OData-Liste des Sets (und ihren Exporten) und das Suchfeld ihrer [Datentabellen](../ui/data-table.md#suchfelder) suchen in denselben Feldern, außer die Entität nennt mit `ISearchable` eigene oder das Set gibt sie mit `AddODataEntity(..., o => o.SearchFields(...))` an. Ohne `Fields` nimmt die globale Suche die Felder des Sets, eine Liste gilt also für alle drei:

```csharp
context.Services.AddODataEntity<Product>("Products", CatalogPermissions.Products.View, o => o.SearchFields(p => p.Name, p => p.Barcode));
context.Services.AddGlobalSearch<Product>(s => s.Subtitle(p => p.Barcode).Url(p => $"/catalog/products/{p.Id}"));
```

| Option | Standard |
|---|---|
| `Fields(...)` | die `SearchFields` des Sets, sonst Pflicht; das erste ist der Titel |
| `Title`, `Subtitle` | erstes Feld, keiner |
| `Url(p => ...)` | `Page`, gesucht nach dem Titel |
| `Page("/list")` | die Listenseite; ein Treffer öffnet sie gesucht nach dem Datensatz |
| `Thumbnail(p => ...)` | keins; Data-URLs über 32 KB bleiben weg |
| `Name`, `Group` | das Entity-Set in Kleinbuchstaben, das Entity-Set |
| `Icon("Person")` | ein Material-Icon-Name (oder SVG-Markup) |
| `Weight(0.8)` | 1 |
| `LeadFields(when, ...)` | keine; siehe [Leitfelder](#leitfelder-für-große-sets) |

### Leitfelder für große Sets

Eine Suche über mehrere Textspalten liest jede Zeile, die der Benutzer sehen darf: kein Datenbankindex beantwortet `LOWER(spalte) LIKE` über eine Handvoll Spalten. Bei ein paar tausend Zeilen dauert das Millisekunden, bei Millionen länger als der Timeout des Providers (`Coworkee:GlobalSearch:ProviderTimeout`, 3 s), und der Benutzer bekommt nichts. `LeadFields` nennt Spalten, die ein Index beantwortet (eine Nummer, ein Schlüssel), und durchsucht sie zuerst allein, wann immer die Bedingung gilt:

```csharp title="MyApp.Shipping/ShippingModule.cs"
context.Services.AddGlobalSearch<Shipment>(s => s
    .Fields(x => x.DisplayNumber, x => x.HouseBill, x => x.ContainerNumber)
    // ein Benutzer, der jede Sendung sieht: zuerst die Nummernspalte, sie liegt in einem Index
    .LeadFields((scope, ct) => scope.GetRequiredService<IShipmentScope>().SeesAllAsync(ct), x => x.Number));
```

Antwortet `when` mit true (es läuft einmal pro Suche, im Scope des Providers, angemeldet als der Benutzer), durchsucht jede Runde zuerst die Leitfelder und schickt ihre Treffer sofort; die übrigen Felder füllen danach den Rest des Limits. Mit fünf Leittreffern laufen die langsamen Runden gar nicht; mit weniger laufen sie trotzdem, und laufen sie in den Timeout, sind die Leittreffer schon unterwegs (der Provider endet mit ihnen als `TimedOut`). Ein Leitfeld darf für ein Suchfeld einspringen, das kein Index beantwortet: `x.Number` für `x.Number ?? x.HouseBill`, wenn `HouseBill` auch durchsucht wird. Leitfelder sind keine eigenen Suchfelder für `$search`, die Datentabellen oder lokale Kopien, und Gesamtzahlen (`totals=true`) zählen weiter die Treffer von `Fields`. Jede Runde kostet eine Abfrage mehr, solange die Leitfelder weniger Treffer finden als das Limit; die Bedingung sollte für kleine Sets also false antworten.

### Zuerst die lokalen Kopien

Ist das Set eine [Client-Entity](../ui/client-entities.md), deren Kopie im Browser vollständig ist, durchsucht die Befehlspalette beim Tippen die Kopie und zeigt ihre Treffer sofort; die Treffer des Servers für dieselben Datensätze kommen ohne Doppelte dazu (gleicher Link oder gleicher Titel und Untertitel). Die Kopie enthält nur Zeilen, die der Benutzer lesen darf. Dafür sagt der Provider dem Client, wie (`GlobalSearchProviderDto.Local` in `/providers`): die Felder als OData-Pfade, die Eigenschaft von Titel und Untertitel und den Link als Vorlage (`/catalog/products/{Id}`) oder die Listenseite. Coworkee findet sie, indem es `Title`, `Subtitle` und `Url` auf zwei erfundenen Zeilen aufruft; sie müssen also Eigenschaften der Zeile selbst lesen (Texte, Ids, Zahlen). Ein Titel aus zwei Werten, ein Link mit einem maskierten oder formatierten Wert oder ein Feld, das kein Eigenschaftspfad ist, lassen das Set beim Server. Eine globale Suche auf einer anderen Entity als dem gespiegelten Set, ein Typfilter (`users:max`) oder Anführungszeichen suchen nur auf dem Server, und eine Kopie zählt erst, wenn eine Seite sie in diesem Besuch gestartet hat (eine Tabelle des Sets).

## Provider der Bibliothek

| Provider | Findet | Berechtigung |
|---|---|---|
| `users` | Name, Benutzername, E-Mail; öffnet den Benutzer | `Identity.Users.View` |
| `roles`, `groups` | Name und Beschreibung | `Identity.Roles.View`, `Identity.Groups.View` |
| `files` | Dateinamen; öffnet die Dateiseite mit der Vorschau | `Files.View` |
| `reports` | Berichte, die der Benutzer sieht (eigene, Mandant, eigene Rollen) | `Reports.View` |
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

Für [Gesamtzahlen](#seiten-und-gesamtzahlen) implementiert ein Provider zusätzlich `Task<long?> CountAsync(GlobalSearchQuery query, GlobalSearchContext context, CancellationToken ct)` (die Vorgabe des Interfaces antwortet null).

## Suchindex (Elasticsearch)

Mit dem [Suchmodul](../modules/search.md) wird ein Index-Alias zum Provider. Der Index kennt die Regeln der Datenbank nicht: Jedes Dokument trägt seinen Mandanten (`tenantId`, ein anderes Feld setzt du mit `TenantField`), und `Where` fügt die Filter hinzu, die ein Benutzer braucht.

```csharp
services.AddGlobalSearchIndex("myapp-products", s => s
    .Fields("name", "barcode").Subtitle("barcode")
    .Url("/catalog/products?id={id}")
    .Permission(CatalogPermissions.Products.View));
```

## Endpunkte

| Endpunkt | Antwort |
|---|---|
| `GET /api/v1/search?q=&types=&limit=&skip=&totals=` | alle Treffer, die besten zuerst, und wie jeder Provider geendet hat (`GlobalSearchResult`) |
| `GET /api/v1/search/stream?q=&types=&limit=&skip=&totals=` | JSON-Zeilen (`application/x-ndjson`): jeder Treffer, sobald er gefunden ist, dann eine Zeile pro Provider mit `Done`, `TimedOut` oder `Failed` |
| `GET /api/v1/search/providers` | die Provider, die der Benutzer nutzen darf, mit `Local` für die Suche in einer lokalen Kopie |
| `GET /api/v1/search/fields` | die angegebenen Suchfelder jedes Sets, das der Benutzer lesen darf, als OData-Pfade, für [Datentabellen](../ui/data-table.md#suchfelder) |

`types` ist eine Komma-Liste von Provider-Namen, `limit` die Treffer pro Provider (`Coworkee:GlobalSearch:DefaultLimit`, Standard 5, höchstens `MaxLimit`, Standard 50). Ein Provider, der nach `Coworkee:GlobalSearch:ProviderTimeout` (3 s) noch sucht, endet als `timedOut`, ohne die anderen aufzuhalten; bricht der Browser die Anfrage ab, hören alle auf. Treffer laufen wie jede Antwort der API durch die [Response-Filter](response-filters.md), ein `ResponseFilter<GlobalSearchHit>` kann also Untertitel vor manchen Benutzern verbergen.

### Seiten und Gesamtzahlen

Eine Ergebnisseite mit einer Tabelle pro Typ und Blätterleiste fragt je einen Typ ab: `skip` lässt die ersten Treffer des Providers aus (höchstens `Coworkee:GlobalSearch:MaxSkip`, Standard 1000; jede Seite liest die Treffer davor mit), `totals=true` ergänzt, wie viele Datensätze insgesamt passen, `GlobalSearchProviderStatus.Total` (im Stream `Total` in der letzten Zeile des Providers). Sets, die mit `AddGlobalSearch` durchsucht werden, zählen die Zeilen, die die Suche finden kann (jedes Wort in einem der Felder, dieselben Zeilen und Mandanten wie die Treffer); ein eigener Provider zählt, indem er `CountAsync` implementiert, sonst bleibt `Total` null.

```csharp
// Seite 3 der Sendungen, 5 pro Seite, mit der Gesamtzahl für "x-y von n"
var result = await search.SearchAsync("S0522", ["shipment"], limit: 5, skip: 10, totals: true);
var shown = result.Hits;                                  // Treffer 11 bis 15
var total = result.Providers.Single().Total;              // z. B. 42
```

Der Stream geht Zeile für Zeile durch das BFF (es puffert nicht) und schickt `X-Accel-Buffering: no` für nginx; andere Proxys vor der App dürfen `application/x-ndjson` auch nicht puffern.

Das MCP-Tool `global_search` (`query`, `types`, `limit`) liefert dasselbe Ergebnis wie der JSON-Endpunkt; es liest nur.
