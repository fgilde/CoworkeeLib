# Search

`Coworkee.Search` describes a full text index (`ISearchIndex`); `Coworkee.Search.Elasticsearch` implements it over the REST API of Elasticsearch. The app owns its documents and decides what goes in.

## Setup

Add the module (template option `elasticsearch`, off by default):

```csharp
[DependsOn(typeof(CoworkeeElasticsearchModule))]
public sealed class MyAppDatabaseModule : CoworkeeModule;
```

The address comes from `Coworkee:Search:Elasticsearch:Url`, else from the connection string `elasticsearch`, else `http://localhost:9200`. User and password may be part of the URL (`http://user:password@host:9200`) or come from `Coworkee:Search:Elasticsearch:Username` and `Password`.

The app host adds an Elasticsearch container only when a service references the Elasticsearch package, and passes it as the connection string `elasticsearch`. `options.ConfigureSearch(es => ...)` changes the container, `options.UseSearch(resource)` uses an existing one instead, and `options.UseKibana()` puts [Kibana](../hosting/aspire.md#kibana) next to the container.

## Usage

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

A schema is an alias and its fields (`Keyword`, `Text`, `Date`, `Long`, `Double`, `Boolean`); fields not listed are stored but not searchable. `Keyword` fields filter, sort and count facets; `Text` fields are analysed and keep a keyword sub field for exact filters and sorting. A document is an id and a `JsonObject` (dotted names become nested objects).

| Member of `ISearchIndex` | |
|---|---|
| `EnsureAsync(schema)` | creates the alias with a first index if missing and adds new fields to the mapping |
| `UpsertAsync(target, documents)` | adds or replaces documents in an alias or index; returns the refused ones (`SearchWriteError`) |
| `DeleteAsync(target, ids)`, `DeleteWhereAsync(target, filters)` | removes documents by id, or every document matching all filters |
| `SearchAsync(alias, query)` | hits, total, facets and a cursor for the next page |
| `SuggestAsync(alias, prefix, fields, filters, size)` | type-ahead: documents whose fields start with or fuzzily match the prefix |
| `CreateIndexAsync(schema)`, `SwapAsync(alias, index)` | rebuild in the background: fill a fresh index, then point the alias at it without downtime (the indexes it pointed at before are removed) |

A `SearchQuery` takes a `Term` (matched against `TextFields`; `Exact` searches it as a phrase, `AllWords` is on by default), `Filters` (`SearchFilter` with a `FilterOperator` such as `Equals`, `Contains`, `FuzzyContains`, `Between` or `HasValue`; several values of `Equals` mean "any of"), `FacetFilters` (selected facet values: they filter the hits while each facet still counts its own field as if nothing were selected), `Facets`, `Sort`, `Size` (default 50), `Cursor` (from the previous result) and `Include` (stored fields to return; empty returns only ids). A refused request throws a `SearchIndexException`; `IsInvalidRequest` tells a bad request (value, cursor) from an index that is not available.

## Global search

An alias joins the [global search](../web/global-search.md#search-index-elasticsearch) (command palette, `/search`, MCP) with `services.AddGlobalSearchIndex(alias, s => s.Fields(...))`.
