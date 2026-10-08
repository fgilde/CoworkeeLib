using System.Text.Json;
using Microsoft.JSInterop;

namespace Coworkee.Client.Blazor.Layout;

/// <summary>Per-browser layout choices (pinned drawer, single expand, right to left); kept in local storage.</summary>
public sealed class LayoutPreferences(IJSRuntime js)
{
    private const string StorageKey = "coworkee.layout";

    public bool Pinned { get; private set; } = true;

    /// <summary>Null until the user chooses; the theme decides until then.</summary>
    public bool? SingleExpand { get; private set; }

    public bool RightToLeft { get; private set; }

    public event Action? Changed;

    public async Task LoadAsync()
    {
        try
        {
            if (await js.InvokeAsync<string?>("localStorage.getItem", StorageKey) is { Length: > 0 } json
                && JsonSerializer.Deserialize<State>(json) is { } state)
            {
                (Pinned, SingleExpand, RightToLeft) = (state.Pinned, state.SingleExpand, state.RightToLeft);
                Changed?.Invoke();
            }
        }
        catch (Exception exception) when (exception is JSException or JsonException or InvalidOperationException)
        {
        }
    }

    public Task SetPinnedAsync(bool pinned) => UpdateAsync(() => Pinned = pinned);

    public Task SetSingleExpandAsync(bool singleExpand) => UpdateAsync(() => SingleExpand = singleExpand);

    public Task SetRightToLeftAsync(bool rightToLeft) => UpdateAsync(() => RightToLeft = rightToLeft);

    private async Task UpdateAsync(Action change)
    {
        change();
        Changed?.Invoke();
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", StorageKey, JsonSerializer.Serialize(new State(Pinned, SingleExpand, RightToLeft)));
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException)
        {
        }
    }

    private sealed record State(bool Pinned, bool? SingleExpand, bool RightToLeft);
}
