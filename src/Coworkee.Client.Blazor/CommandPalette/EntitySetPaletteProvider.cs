using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Security;

namespace Coworkee.Client.Blazor.CommandPalette;

/// <summary>Searches the records of one OData set once two letters are typed.</summary>
internal sealed class EntitySetPaletteProvider<T>(CommandPaletteEntitySet<T> set, IODataClient odata, PermissionStore permissions, CoworkeeLocalizer l)
    : ICommandPaletteProvider
{
    private const int Order = 10;

    public async Task<IEnumerable<CommandPaletteItem>> GetItemsAsync(string query, CancellationToken cancellationToken)
    {
        if (query.Trim().Length < 2 || (set.Permission is not null && !await permissions.HasAsync(set.Permission)))
        {
            return [];
        }

        try
        {
            var page = await odata.QueryAsync<T>(set.EntitySet, new ODataQuery { Filter = ODataFilter.Search(query.Trim(), set.SearchFields), Top = set.Top, Count = false },
                cancellationToken);
            return page.Items.Select(row => new CommandPaletteItem(l[set.Group ?? set.EntitySet], set.Title(row))
            {
                Icon = set.Icon,
                Subtitle = set.Subtitle?.Invoke(row),
                Href = set.Href?.Invoke(row) ?? SearchLink(set.Title(row)),
                Matched = true,
                Order = Order,
            });
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
            return [];
        }
    }

    // the list page searched for the record, through the table's URL state
    private string SearchLink(string title) =>
        $"{set.Page}?{set.EntitySet.ToLowerInvariant()}={Uri.EscapeDataString(new DataTableState(title, [], []).Encode())}";
}
