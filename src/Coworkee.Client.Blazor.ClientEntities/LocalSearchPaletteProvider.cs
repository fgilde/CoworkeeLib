using System.Text.RegularExpressions;
using Coworkee.Client.Blazor.CommandPalette;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Search;
using Coworkee.Contracts.Search;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Client.Blazor.ClientEntities;

/// <summary>
/// Records of the server's global search found in the local copies first: for every provider whose set is local here (and that tells how,
/// <see cref="GlobalSearchProviderDto.Local"/>) the palette shows hits at once; the server's hits of the same records join without doubles. A copy holds only
/// rows the user may read, so nothing shows that the server would not.
/// </summary>
internal sealed partial class LocalSearchPaletteProvider(ClientEntities entities, IServiceProvider services) : ICommandPaletteProvider
{
    // as the palette: at most eight per group, the server's global search from two letters on, records rank as group 10
    private const int Limit = 8;
    private const int MinQuery = 2;
    private const int RecordsOrder = 10;

    private Task<IReadOnlyList<GlobalSearchProviderDto>>? _providers;

    public async Task<IEnumerable<CommandPaletteItem>> GetItemsAsync(string query, CancellationToken cancellationToken)
    {
        var text = query.Trim();
        if (text.Length == 0)
        {
            // the palette just opened: the providers are asked again with the first letters, so permissions changed meanwhile count
            _providers = null;
            return [];
        }

        // a type filter or quotes ("exact") are the server's syntax
        if (text.Length < MinQuery || text.IndexOfAny([':', '"']) >= 0 || !entities.Sets.Any(s => entities.AnswersLocally(s.EntitySet))
            || services.GetService<IGlobalSearchApi>() is not { } api)
        {
            return [];
        }

        IReadOnlyList<GlobalSearchProviderDto> providers;
        try
        {
            providers = await (_providers ??= api.GetProvidersAsync(CancellationToken.None));
        }
        catch (Exception exception) when (exception is Api.ApiException or HttpRequestException or System.Text.Json.JsonException)
        {
            _providers = null;
            return [];
        }

        var items = new List<CommandPaletteItem>();
        foreach (var provider in providers)
        {
            if (provider.Local is { } local && entities.AnswersLocally(local.EntitySet) && entities.Find(local.EntitySet) is { } set)
            {
                items.AddRange(await FindAsync(set, provider, local, text, cancellationToken));
            }
        }

        return items;
    }

    // as the server: values that start with the text first, then those containing every word
    private static async Task<IEnumerable<CommandPaletteItem>> FindAsync(ClientEntitySet set, GlobalSearchProviderDto provider, GlobalSearchLocalDto local, string text,
        CancellationToken cancellationToken)
    {
        var lower = ODataFilter.Literal(text.ToLowerInvariant());
        var prefix = "(" + string.Join(" or ", local.Fields.Select(f => $"startswith(tolower({f}),{lower})")) + ")";
        var words = ODataFilter.And([.. text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word => ODataFilter.Search(word, local.Fields))])!;
        List<string> paths = [local.TitleField, .. local.SubtitleField is { } subtitle ? [subtitle] : Array.Empty<string>(),
            .. local.Url is { } url ? Placeholder().Matches(url).Select(m => m.Groups[1].Value) : []];
        var rows = new List<string?[]>();
        foreach (var filter in new[] { prefix, words })
        {
            if (await set.LocalValuesAsync(filter, local.TitleField, Limit, paths, cancellationToken) is not { } found)
            {
                return [];
            }

            rows.AddRange(found);
        }

        return rows.DistinctBy(r => string.Join('\u0001', r)).Take(Limit).Select(row =>
        {
            var title = row[0] ?? string.Empty;
            return new CommandPaletteItem(provider.Group, title)
            {
                Icon = SearchIcons.Resolve(provider.Icon),
                Subtitle = local.SubtitleField is null ? null : row[1],
                Href = Link(local, row, paths, title),
                Matched = true,
                Order = RecordsOrder,
            };
        });
    }

    /// <summary>The server's link: its template filled in, else the list page searched for the title in the table's URL state (ODataSearchProvider.PageLink).</summary>
    internal static string? Link(GlobalSearchLocalDto local, string?[] row, List<string> paths, string title) =>
        local.Url is { } url ? Placeholder().Replace(url, m => row[paths.IndexOf(m.Groups[1].Value)] ?? string.Empty)
        : local.Page is { } page ? $"{page}{(page.Contains('?') ? '&' : '?')}{local.EntitySet.ToLowerInvariant()}={new DataTableState(title, [], []).Encode()}"
        : null;

    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex Placeholder();
}
