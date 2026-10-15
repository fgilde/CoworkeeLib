# Command palette

**Ctrl+K** (**Cmd+K** on a Mac) or the search button in the app bar opens the command palette: one box for pages, records, actions and the assistant. It works without a mouse: type, move with the arrow keys, open with **Enter**, close with **Esc**. On a phone it opens as a sheet at the top.

What it finds:

- **Pages** of the navigation the user may open (the same permission, feature and organisation rules as the menu).
- **Records** of the entity sets you register, searched on the server once two letters are typed.
- **Actions**: dark or light mode, another theme, another language, another active role, and your own commands like *New product*.
- **Ask the assistant: …** as the last entry, when the [assistant](../modules/ai.md) is set up and the user may chat. It opens the assistant window with the text in the input.

The search is fuzzy: `nwprd` finds *New product*, and the matching letters are highlighted. With nothing typed, the palette shows the links the user opened last.

## Your own entries

```csharp
// an action: a link or any code
builder.Services.AddCommandPaletteCommand(new CommandPaletteCommand("New product", Icons.Material.Outlined.Add,
    CatalogPermissions.Products.Create, Href: "/catalog/products?new=1"));

// records of an OData set; a hit opens the list searched for it
builder.Services.AddCommandPaletteEntitySet(new CommandPaletteEntitySet<ProductDto>("Products", "/catalog/products", p => p.Name,
    [nameof(ProductDto.Name), nameof(ProductDto.Barcode)])
{
    Permission = CatalogPermissions.Products.View,
    Icon = Icons.Material.Outlined.Inventory2,
    Subtitle = p => p.Brand?.Name,
});
```

`?new=1` on a page with a `CoworkeeDataTable` opens its create dialog once, so a *New ...* command needs nothing more. Give a record its own page with `Href = p => $"/catalog/products/{p.Id}"`.

For anything else, write a provider. It gets the text typed so far and returns items; the palette filters and ranks them unless you set `Matched = true` because you searched yourself.

```csharp
public sealed class HelpArticles(IHelpApi help) : ICommandPaletteProvider
{
    public async Task<IEnumerable<CommandPaletteItem>> GetItemsAsync(string query, CancellationToken cancellationToken) =>
        query.Length < 3 ? [] : (await help.SearchAsync(query, cancellationToken))
            .Select(a => new CommandPaletteItem("Help", a.Title) { Icon = Icons.Material.Outlined.HelpOutline, Href = a.Url, Matched = true });
}

builder.Services.AddCommandPaletteProvider<HelpArticles>();
```

`Order` sorts the groups (pages 0, records 10, actions 20, the assistant 1000), `Keywords` adds words the search looks at, and `Run` replaces the link with your own code.
