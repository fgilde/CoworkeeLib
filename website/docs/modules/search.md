# Search

`Coworkee.Search` describes an index; `Coworkee.Search.Elasticsearch` implements it. The app owns its documents and decides what goes in.

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

Queries support full text, filters, sorting, facets and cursor paging. `CreateIndexAsync` and `SwapAsync` rebuild an index in the background and switch the alias without downtime.

The connection comes from the connection string `elasticsearch` or `Coworkee:Search:Elasticsearch:Url`; user and password may be part of the URL. The app host adds Elasticsearch only when a service references the Elasticsearch package.
