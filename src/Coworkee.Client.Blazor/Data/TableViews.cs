using System.Text.Json;
using Microsoft.JSInterop;

namespace Coworkee.Client.Blazor.Data;

public sealed record SavedTableView(string Name, DataTableState State);

/// <summary>Named table states per entity set.</summary>
// ponytail: kept in the browser's local storage, so views do not follow the user to other devices; move to a user setting when that matters
public sealed class TableViews(IJSRuntime js)
{
    public async Task<IReadOnlyList<SavedTableView>> GetAsync(string entitySet)
    {
        try
        {
            return await js.InvokeAsync<string?>("localStorage.getItem", Key(entitySet)) is { Length: > 0 } json
                ? JsonSerializer.Deserialize<List<SavedTableView>>(json, JsonSerializerOptions.Web) ?? []
                : [];
        }
        catch (Exception exception) when (exception is JSException or JsonException or InvalidOperationException)
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<SavedTableView>> SaveAsync(string entitySet, string name, DataTableState state)
    {
        var views = (await GetAsync(entitySet)).Where(v => !string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)).Append(new SavedTableView(name, state))
            .OrderBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        await StoreAsync(entitySet, views);
        return views;
    }

    public async Task<IReadOnlyList<SavedTableView>> DeleteAsync(string entitySet, string name)
    {
        var views = (await GetAsync(entitySet)).Where(v => v.Name != name).ToList();
        await StoreAsync(entitySet, views);
        return views;
    }

    private async Task StoreAsync(string entitySet, IReadOnlyList<SavedTableView> views)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", Key(entitySet), JsonSerializer.Serialize(views, JsonSerializerOptions.Web));
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException)
        {
        }
    }

    private static string Key(string entitySet) => $"coworkee.views.{entitySet}";
}
