# Suche

`Coworkee.Search` beschreibt einen Volltextindex (`ISearchIndex`); `Coworkee.Search.Elasticsearch` setzt ihn über die REST-API von Elasticsearch um. Die App besitzt ihre Dokumente und entscheidet, was hineinkommt.

## Einrichtung

Füge das Modul hinzu (Vorlagenoption `elasticsearch`, standardmäßig aus):

```csharp
[DependsOn(typeof(CoworkeeElasticsearchModule))]
public sealed class MyAppDatabaseModule : CoworkeeModule;
```

Die Adresse kommt aus `Coworkee:Search:Elasticsearch:Url`, sonst aus dem Connection-String `elasticsearch`, sonst `http://localhost:9200`. Benutzer und Passwort dürfen in der URL stehen (`http://user:password@host:9200`) oder aus `Coworkee:Search:Elasticsearch:Username` und `Password` kommen.

Der AppHost fügt einen Elasticsearch-Container nur hinzu, wenn ein Dienst das Elasticsearch-Paket referenziert, und gibt ihn als Connection-String `elasticsearch` weiter. `options.ConfigureSearch(es => ...)` ändert den Container, `options.UseSearch(resource)` nimmt stattdessen einen vorhandenen, und `options.UseKibana()` stellt [Kibana](../hosting/aspire.md#kibana) neben den Container.

## Verwendung

```csharp
var schema = new SearchSchema("myapp-products",
[
    new SearchField("name", SearchFieldType.Text),
    new SearchField("brand", SearchFieldType.Keyword),
    new SearchField("rate", SearchFieldType.Double),
]);

await index.EnsureAsync(schema, ct);
await index.UpsertAsync(schema.Alias, [new SearchDocument(product.Id.ToString(), body)], ct);
```

Ein Schema ist ein Alias und seine Felder (`Keyword`, `Text`, `Date`, `Long`, `Double`, `Boolean`); nicht genannte Felder werden gespeichert, aber nicht durchsucht. `Keyword`-Felder filtern, sortieren und zählen Facetten; `Text`-Felder werden analysiert und behalten ein Keyword-Unterfeld für exakte Filter und Sortierung. Ein Dokument ist eine Id und ein `JsonObject` (Namen mit Punkt werden verschachtelte Objekte).

| Member von `ISearchIndex` | |
|---|---|
| `EnsureAsync(schema)` | legt den Alias mit einem ersten Index an, falls er fehlt, und ergänzt neue Felder im Mapping |
| `UpsertAsync(target, documents)` | fügt Dokumente in einen Alias oder Index ein oder ersetzt sie; gibt die abgelehnten zurück (`SearchWriteError`) |
| `DeleteAsync(target, ids)`, `DeleteWhereAsync(target, filters)` | entfernt Dokumente nach Id oder jedes Dokument, das alle Filter erfüllt |
| `SearchAsync(alias, query)` | Treffer, Gesamtzahl, Facetten und ein Cursor für die nächste Seite |
| `SuggestAsync(alias, prefix, fields, filters, size)` | Vorschläge beim Tippen: Dokumente, deren Felder mit dem Präfix beginnen oder ihm unscharf gleichen |
| `CreateIndexAsync(schema)`, `SwapAsync(alias, index)` | Neuaufbau im Hintergrund: einen frischen Index füllen, dann den Alias ohne Ausfall darauf umschalten (die Indizes, auf die er vorher zeigte, werden entfernt) |

Eine `SearchQuery` nimmt einen `Term` (gesucht in `TextFields`; `Exact` sucht ihn als Phrase, `AllWords` ist standardmäßig an), `Filters` (`SearchFilter` mit einem `FilterOperator` wie `Equals`, `Contains`, `FuzzyContains`, `Between` oder `HasValue`; mehrere Werte bei `Equals` bedeuten „einer davon“), `FacetFilters` (gewählte Facettenwerte: Sie filtern die Treffer, während jede Facette ihr eigenes Feld zählt, als wäre nichts gewählt), `Facets`, `Sort`, `Size` (Standard 50), `Cursor` (aus dem vorigen Ergebnis) und `Include` (gespeicherte Felder, die zurückkommen; leer gibt nur Ids). Eine abgelehnte Anfrage wirft eine `SearchIndexException`; `IsInvalidRequest` unterscheidet eine fehlerhafte Anfrage (Wert, Cursor) von einem nicht erreichbaren Index.

## Globale Suche

Mit `services.AddGlobalSearchIndex(alias, s => s.Fields(...))` wird ein Alias Teil der [globalen Suche](../web/global-search.md#suchindex-elasticsearch) (Befehlspalette, `/search`, MCP).
