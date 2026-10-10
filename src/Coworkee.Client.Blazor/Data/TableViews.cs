using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Contracts.Settings;
using Microsoft.JSInterop;

namespace Coworkee.Client.Blazor.Data;

public sealed record SavedTableView(string Name, DataTableState State)
{
    /// <summary>Opens with the table when its address carries no state of its own.</summary>
    public bool IsDefault { get; init; }
}

/// <summary>Named table states per table; replace it in DI to keep them somewhere else.</summary>
public interface ITableViewStore
{
    Task<IReadOnlyList<SavedTableView>> GetAsync(string table);

    /// <summary>Adds the view or replaces the one with its name; a default view takes the default from the others.</summary>
    Task<IReadOnlyList<SavedTableView>> SaveAsync(string table, SavedTableView view);

    Task<IReadOnlyList<SavedTableView>> DeleteAsync(string table, string name);
}

/// <summary>Keeps the views in the user setting <see cref="TableSettings.Views"/>, so they follow the user to every device.</summary>
/// <remarks>Without a signed in user or without the settings module the views stay in the browser's local storage.</remarks>
public sealed class TableViews(ICoworkeeApi api, IJSRuntime js) : ITableViewStore
{
    private const string LocalKey = "coworkee.views";

    private Task<Dictionary<string, List<SavedTableView>>>? _loading;
    private bool _local;

    public async Task<IReadOnlyList<SavedTableView>> GetAsync(string table) =>
        (await AllAsync()).GetValueOrDefault(table) ?? [];

    public async Task<IReadOnlyList<SavedTableView>> SaveAsync(string table, SavedTableView view)
    {
        var views = (await GetAsync(table))
            .Where(v => !string.Equals(v.Name, view.Name, StringComparison.OrdinalIgnoreCase))
            .Select(v => view.IsDefault ? v with { IsDefault = false } : v)
            .Append(view)
            .OrderBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return await StoreAsync(table, views);
    }

    public async Task<IReadOnlyList<SavedTableView>> DeleteAsync(string table, string name) =>
        await StoreAsync(table, (await GetAsync(table)).Where(v => v.Name != name).ToList());

    private Task<Dictionary<string, List<SavedTableView>>> AllAsync() => _loading ??= LoadAsync();

    private async Task<Dictionary<string, List<SavedTableView>>> LoadAsync()
    {
        string? json = null;
        try
        {
            var setting = (await api.GetSettingsAsync(SettingScope.User)).FirstOrDefault(s => s.Name == TableSettings.Views);
            _local = setting is null;
            json = setting?.Value;
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
            _local = true;
        }

        if (_local)
        {
            json = await LocalAsync(() => js.InvokeAsync<string?>("localStorage.getItem", LocalKey).AsTask());
        }

        try
        {
            return json is { Length: > 0 } ? JsonSerializer.Deserialize<Dictionary<string, List<SavedTableView>>>(json, JsonSerializerOptions.Web) ?? [] : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task<IReadOnlyList<SavedTableView>> StoreAsync(string table, List<SavedTableView> views)
    {
        var all = await AllAsync();
        all[table] = views;
        var json = JsonSerializer.Serialize(all, JsonSerializerOptions.Web);
        if (_local)
        {
            await LocalAsync(async () => await js.InvokeVoidAsync("localStorage.setItem", LocalKey, json));
        }
        else
        {
            await api.SetSettingsAsync(SettingScope.User, new Dictionary<string, string?>(StringComparer.Ordinal) { [TableSettings.Views] = json });
        }

        return views;
    }

    private static async Task<TResult?> LocalAsync<TResult>(Func<Task<TResult>> storage)
    {
        try
        {
            return await storage();
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException)
        {
            return default;
        }
    }

    private static Task LocalAsync(Func<Task> storage) => LocalAsync(async () =>
    {
        await storage();
        return true;
    });
}
