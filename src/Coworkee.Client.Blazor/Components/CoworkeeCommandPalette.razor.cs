using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.CommandPalette;
using Coworkee.Client.Blazor.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

public sealed record PaletteHit(CommandPaletteItem Item, int Score, IReadOnlyList<int> Positions);

/// <summary>
/// Ctrl+K / Cmd+K or the search button in the app bar: pages, records, actions and the assistant in one keyboard-first list.
/// Apps add items with AddCommandPaletteProvider, AddCommandPaletteCommand and AddCommandPaletteEntitySet.
/// </summary>
public partial class CoworkeeCommandPalette : IAsyncDisposable
{
    private const int PerGroup = 8;
    private const int DebounceMilliseconds = 150;

    private List<PaletteHit> _hits = [];
    private string _query = string.Empty;
    private int _active;
    private bool _open;
    private bool _focus;
    private bool _searching;
    private ElementReference _input;
    private CancellationTokenSource? _search;
    private RecentPaletteItems? _recent;
    private IJSObjectReference? _hotkey;
    private DotNetObjectReference<CoworkeeCommandPalette>? _self;

    [Inject] private IEnumerable<ICommandPaletteProvider> Providers { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private Navigation.CoworkeePages Links { get; set; } = null!;

    [Inject] private IJSRuntime JS { get; set; } = null!;

    [Inject] private IScrollManager Scroll { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    private RecentPaletteItems Recent => _recent ??= new RecentPaletteItems(JS);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _self = DotNetObjectReference.Create(this);
            try
            {
                var module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/Coworkee.Client.Blazor/coworkee.js");
                _hotkey = await module.InvokeAsync<IJSObjectReference>("onHotkey", _self, "k");
            }
            catch (JSException)
            {
            }
        }

        if (_focus && _open)
        {
            _focus = false;
            await _input.FocusAsync();
        }
    }

    [JSInvokable]
    public Task ToggleFromHotkey() => InvokeAsync(async () =>
    {
        if (_open)
        {
            Close();
        }
        else
        {
            await OpenAsync();
        }
    });

    public async Task OpenAsync()
    {
        _open = true;
        _focus = true;
        _query = string.Empty;
        await SearchAsync(debounce: false);
    }

    public void Close()
    {
        _search?.Cancel();
        _open = false;
        StateHasChanged();
    }

    private Task QueryChangedAsync(ChangeEventArgs args)
    {
        _query = args.Value?.ToString() ?? string.Empty;
        return SearchAsync(debounce: true);
    }

    private async Task KeyAsync(KeyboardEventArgs args)
    {
        switch (args.Key)
        {
            case "ArrowDown":
                await MoveAsync(1);
                break;
            case "ArrowUp":
                await MoveAsync(-1);
                break;
            case "Enter" when _hits.Count > 0:
                await RunAsync(_hits[Math.Clamp(_active, 0, _hits.Count - 1)].Item);
                break;
            case "Escape":
                Close();
                break;
        }
    }

    private async Task MoveAsync(int step)
    {
        if (_hits.Count == 0)
        {
            return;
        }

        _active = (_active + step + _hits.Count) % _hits.Count;
        try
        {
            await Scroll.ScrollIntoViewAsync($"#cw-palette-item-{_active}", ScrollBehavior.Auto);
        }
        catch (JSException)
        {
        }
    }

    private void Hover(int index) => _active = index;

    private async Task RunAsync(CommandPaletteItem item)
    {
        Close();
        await Recent.AddAsync(item);
        try
        {
            if (item.Run is { } run)
            {
                await run();
            }
            else if (item.Href is { } href)
            {
                Nav.NavigateTo(Links.Href(href) ?? href);
            }
        }
        catch (ApiException exception)
        {
            Snackbar.Add(SnackbarApiExtensions.Describe(exception), Severity.Error);
        }
    }

    private async Task SearchAsync(bool debounce)
    {
        if (_search is not null)
        {
            await _search.CancelAsync();
        }

        var search = _search = new CancellationTokenSource();
        var query = _query.Trim();
        _searching = true;
        try
        {
            if (debounce && query.Length > 0)
            {
                await Task.Delay(DebounceMilliseconds, search.Token);
            }

            var found = await Task.WhenAll(Providers.Select(p => ItemsAsync(p, query, search.Token)));
            var items = found.SelectMany(i => i);
            if (query.Length == 0)
            {
                items = (await Recent.GetAsync()).Concat(items);
            }

            if (search.IsCancellationRequested)
            {
                return;
            }

            _hits = Rank(items, query);
            _active = 0;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (search == _search)
            {
                _searching = false;
            }
        }

        StateHasChanged();
    }

    /// <summary>Recent first, then groups by order and best hit; within a group the best hits, at most eight.</summary>
    internal static List<PaletteHit> Rank(IEnumerable<CommandPaletteItem> items, string query) =>
        items.Select(i => Score(i, query)).OfType<PaletteHit>()
            .GroupBy(h => h.Item.Group)
            .OrderBy(g => g.Key == RecentPaletteItems.Group ? 0 : 1)
            .ThenBy(g => g.Min(h => h.Item.Order))
            .ThenByDescending(g => g.Max(h => h.Score))
            .SelectMany(g => g.OrderByDescending(h => h.Score).Take(PerGroup))
            .ToList();

    private static PaletteHit? Score(CommandPaletteItem item, string query)
    {
        var title = FuzzyMatch.Match(query, item.Title);
        if (title is not null)
        {
            return new PaletteHit(item, title.Score, title.Positions);
        }

        if (item.Matched)
        {
            return new PaletteHit(item, 0, []);
        }

        var other = FuzzyMatch.Match(query, $"{item.Keywords} {item.Subtitle}");
        return other is null ? null : new PaletteHit(item, other.Score / 2, []);
    }

    private static async Task<IEnumerable<CommandPaletteItem>> ItemsAsync(ICommandPaletteProvider provider, string query, CancellationToken cancellationToken)
    {
        try
        {
            return await provider.GetItemsAsync(query, cancellationToken);
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException or JSException)
        {
            return [];
        }
    }

    public async ValueTask DisposeAsync()
    {
        _search?.Cancel();
        _search?.Dispose();
        try
        {
            if (_hotkey is not null)
            {
                await _hotkey.InvokeVoidAsync("dispose");
                await _hotkey.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
        }

        _self?.Dispose();
    }
}
