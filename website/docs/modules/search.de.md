# Suche

`Coworkee.Search` beschreibt einen Index; `Coworkee.Search.Elasticsearch` setzt ihn um. Die App besitzt ihre Dokumente und entscheidet, was hineinkommt.

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

Abfragen können Volltext, Filter, Sortierung, Facetten und Blättern per Cursor. `CreateIndexAsync` und `SwapAsync` bauen einen Index im Hintergrund neu und schalten den Alias ohne Ausfall um.

Die Verbindung kommt aus dem Connection-String `elasticsearch` oder `Coworkee:Search:Elasticsearch:Url`; Benutzer und Passwort dürfen in der URL stehen. Der AppHost fügt Elasticsearch nur hinzu, wenn ein Dienst das Elasticsearch-Paket referenziert.
