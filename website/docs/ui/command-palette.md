# Command palette

**Ctrl+K** (**Cmd+K** on a Mac) or the search button in the app bar opens the command palette: one box for pages, records, actions and the assistant. It works without a mouse: type, move with the arrow keys, open with **Enter**, close with **Esc**. On a phone it opens as a sheet at the top.

What it finds:

- **Pages** of the navigation the user may open (the same permission, feature and organisation rules as the menu).
- **Records** from the [global search](../web/global-search.md) of the server once two letters are typed: users, files, reports and the sets your modules register with `AddGlobalSearch`. They show group by group as the providers find them, the entry you chose stays chosen, and *Show all results* opens `/search?q=` with a filter per type.
- **Actions**: dark or light mode, another theme, another language, another active role, and your own commands like *New product*.
- **Chat**: *Chat with…* picks a person and opens a [mini chat](../modules/chat.md#mini-chats); `@name` lists the matching people to chat with right away (with the Chat module and its permission).
- **Ask the assistant: …** as the last entry, when the [assistant](../modules/ai.md) is set up and the user may chat. It opens the assistant window with the text in the input.

The search is fuzzy: `nwprd` finds *New product*, and the matching letters are highlighted. With nothing typed, the palette shows the links the user opened last (*Recent*, kept in the browser).

## Your own entries

```csharp
// an action: a link or any code
builder.Services.AddCommandPaletteCommand(new CommandPaletteCommand("New product", Icons.Material.Outlined.Add,
    CatalogPermissions.Products.Create, Href: "/catalog/products?new=1"));
```

`Permission` hides the command without it, `Keywords` adds search words, and `Run` (it gets the `IServiceProvider`) runs code instead of `Href`. `?new=1` on a page with a `CoworkeeDataTable` and an `OnCreate` opens its create dialog once (with `CreatePermission`, only for users who have it), so a *New ...* command needs nothing more. Records are registered on the server, next to their OData set, see [Global search](../web/global-search.md).

For anything else that lives in the browser, write a provider (data of the server belongs in a global search provider). It gets the text typed so far and returns items; the palette filters and ranks them unless you set `Matched = true` because you searched yourself.

```csharp
public sealed class HelpArticles(IHelpApi help) : ICommandPaletteProvider
{
    public async Task<IEnumerable<CommandPaletteItem>> GetItemsAsync(string query, CancellationToken cancellationToken) =>
        query.Length < 3 ? [] : (await help.SearchAsync(query, cancellationToken))
            .Select(a => new CommandPaletteItem("Help", a.Title) { Icon = Icons.Material.Outlined.HelpOutline, Href = a.Url, Matched = true });
}

builder.Services.AddCommandPaletteProvider<HelpArticles>();
```

`Order` sorts the groups by their smallest value (pages 0, records 10, actions 20, the assistant last), `Keywords` adds words the search looks at, `Subtitle` is a second line, and `Run` replaces the link with your own code. Items with a link come back under *Recent* once used.
