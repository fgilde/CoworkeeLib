# Global search

One search over everything a user may read: users, roles, groups, files, reports, chats and the app's own records. The command palette (**Ctrl+K**) shows the hits group by group as they arrive, `/search?q=` lists all of them with a filter per type, and the MCP tool `global_search` hands them to AI clients.

## What a search finds

| Typed | Finds |
|---|---|
| `max` | values that start with *max* first, then values that contain it |
| `max power` | records where every word is found in one of the searched fields |
| `"Max Power"` | only values that equal the text |
| `users:max` or `type:users max` | only in one provider; `user:` and `prod:` work too (the start of a provider name) |
| a GUID | the record with that id |

Every hit gets a score from 0 to 1 by the same rules (equal 1, prefix about 0.85, word start about 0.65, part about 0.45, all words 0.3), so hits of different providers rank together. A provider's `Weight` scales its scores.

## Records of your OData sets

A set registered with `AddODataEntity` becomes searchable with one line in the same module:

```csharp title="MyApp.Catalog/CatalogModule.cs"
context.Services.AddODataEntity<Product>("Products", CatalogPermissions.Products.View);
context.Services.AddGlobalSearch<Product>(s => s
    .Fields(p => p.Name, p => p.Barcode, p => p.Description)
    .Subtitle(p => p.Barcode)
    .Thumbnail(p => p.ImageDataUrl)
    .Page("/catalog/products")
    .Icon("ViewCarousel"));
```

The search sees exactly what the OData list of the set returns: its permission, the organisation, [row rules](odata.md#row-level-rules) and soft deletion. Searching a hidden property fails at start. It runs in two rounds: values that start with the text (or the record of a typed id) come at once, values that contain every word follow.

Fields may read through a navigation (`t => t.Customer!.Name`): they are read in the query and follow the [navigation rules](odata.md#navigations-follow-their-target), so a hidden target neither matches nor shows. `Title`, `Subtitle`, `Url` and `Thumbnail` run on the loaded row, which has no navigations; use fields for values of other rows.

`$search` on the set's OData list (and its exports) looks in the same fields, unless the entity names its own with `ISearchable`; list a field here when the table's search box should find it.

| Option | Default |
|---|---|
| `Fields(...)` | required; the first is the title |
| `Title`, `Subtitle` | first field, none |
| `Url(p => ...)` | `Page` searched for the title |
| `Page("/list")` | the list page; a hit opens it searched for the record |
| `Thumbnail(p => ...)` | none; data URLs over 32 KB are left out |
| `Name`, `Group` | the entity set in lower case, the entity set |
| `Icon("Person")` | a Material icon name (or SVG markup) |
| `Weight(0.8)` | 1 |

## Providers of the library

| Provider | Finds | Permission |
|---|---|---|
| `users` | name, user name, e-mail; opens the user | `Identity.Users.View` |
| `roles`, `groups` | name and description | `Identity.Roles.View`, `Identity.Groups.View` |
| `files` | file names; opens the files page with the preview | `Files.View` |
| `reports` | reports the user sees (own, organisation, own roles) | `Reports.View` |
| `chats` | the user's own conversations whose messages contain the words | `Chat.Use` |

Pages and actions stay in the palette itself (they are known in the browser).

## Own providers

Anything else, e.g. a remote system, implements `IGlobalSearchProvider`. Providers are singletons; each search gets its own scope, so providers run side by side.

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

## Search index (Elasticsearch)

With the [search module](../modules/search.md) an index alias becomes a provider. The index cannot apply the rules of the database: each document carries its organisation (`tenantId`, set another field with `TenantField`), and `Where` adds the filters a user needs.

```csharp
services.AddGlobalSearchIndex("myapp-products", s => s
    .Fields("name", "barcode").Subtitle("barcode")
    .Url("/catalog/products?id={id}")
    .Permission(CatalogPermissions.Products.View));
```

## Endpoints

| Endpoint | Answer |
|---|---|
| `GET /api/v1/search?q=&types=&limit=` | all hits best first and how each provider ended (`GlobalSearchResult`) |
| `GET /api/v1/search/stream?q=&types=&limit=` | JSON lines (`application/x-ndjson`): each hit as soon as it is found, then one line per provider with `Done`, `TimedOut` or `Failed` |
| `GET /api/v1/search/providers` | the providers the user may use |

`types` is a comma list of provider names, `limit` the hits per provider (`Coworkee:GlobalSearch:DefaultLimit`, default 5, at most `MaxLimit`, default 50). A provider still busy after `Coworkee:GlobalSearch:ProviderTimeout` (3 s) ends as `timedOut` without holding up the others; a request the browser cancels stops all of them. Hits pass the [response filters](response-filters.md) like any answer of the API, so a `ResponseFilter<GlobalSearchHit>` can hide subtitles from some users.

The stream goes through the BFF line by line (it does not buffer) and sends `X-Accel-Buffering: no` for nginx; other proxies in front of the app must not buffer `application/x-ndjson` either.

The MCP tool `global_search` (`query`, `types`, `limit`) returns the same result as the JSON endpoint; it is read only.
