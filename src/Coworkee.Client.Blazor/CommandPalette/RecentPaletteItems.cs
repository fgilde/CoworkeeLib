using System.Text.Json;
using Microsoft.JSInterop;

namespace Coworkee.Client.Blazor.CommandPalette;

/// <summary>The last links opened from the palette, per browser in local storage; shown while nothing is typed.</summary>
public sealed class RecentPaletteItems(IJSRuntime js)
{
    public const string Group = "Recent";
    private const string StorageKey = "coworkee.palette.recent";
    private const int Max = 6;

    public async Task<IReadOnlyList<CommandPaletteItem>> GetAsync()
    {
        try
        {
            return await js.InvokeAsync<string?>("localStorage.getItem", StorageKey) is { Length: > 0 } json
                ? (JsonSerializer.Deserialize<List<Recent>>(json) ?? []).Select(r => new CommandPaletteItem(Group, r.Title) { Icon = r.Icon, Href = r.Href, Subtitle = r.Subtitle }).ToList()
                : [];
        }
        catch (Exception exception) when (exception is JSException or JsonException or InvalidOperationException)
        {
            return [];
        }
    }

    public async Task AddAsync(CommandPaletteItem item)
    {
        if (item.Href is null || item.Run is not null)
        {
            return;
        }

        var recent = (await GetAsync()).Where(r => r.Href != item.Href).Prepend(item).Take(Max)
            .Select(r => new Recent(r.Title, r.Icon, r.Href!, r.Subtitle)).ToList();
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", StorageKey, JsonSerializer.Serialize(recent));
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException)
        {
        }
    }

    private sealed record Recent(string Title, string? Icon, string Href, string? Subtitle);
}
