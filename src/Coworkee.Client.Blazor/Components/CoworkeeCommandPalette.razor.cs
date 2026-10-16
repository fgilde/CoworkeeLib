using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.CommandPalette;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Search;
using Coworkee.Contracts.Search;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components;

public sealed record PaletteHit(CommandPaletteItem Item, int Score, IReadOnlyList<int> Positions);

/// <summary>
/// Ctrl+K / Cmd+K or the search button in the app bar: pages, records, actions and the assistant in one keyboard-first list.
/// Apps add items with AddCommandPaletteProvider and AddCommandPaletteCommand; records come from the global search of the server
/// (AddGlobalSearch there), group by group as the providers find them.
/// </summary>
public partial class CoworkeeCommandPalette : IAsyncDisposable
{
    private const int PerGroup = 8;
    private const int DebounceMilliseconds = 150;
    private const int MinServerQuery = 2;
    private const int RecordsOrder = 10;
    private const int AllResultsOrder = 900;

    private List<PaletteHit> _hits = [];
    private List<PaletteHit> _local = [];
    private List<PaletteHit> _remote = [];
    private bool _streaming;
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

    [Inject] private IServiceProvider Services { get; set; } = null!;

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

            _local = [.. items.Select(i => Score(i, query)).OfType<PaletteHit>()];
            _remote = [];
            _hits = [];
            _active = 0;
            Show(query);
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
        if (query.Length >= MinServerQuery && Services.GetService<IGlobalSearchApi>() is { } api)
        {
            await StreamAsync(api, query, search.Token);
        }
    }

    /// <summary>Hits of the server's global search join the list as they arrive; the selected entry stays selected.</summary>
    private async Task StreamAsync(IGlobalSearchApi api, string query, CancellationToken cancellationToken)
    {
        var remote = _remote;
        _streaming = true;
        StateHasChanged();
        try
        {
            await foreach (var found in api.StreamAsync(query, cancellationToken: cancellationToken))
            {
                if (found.Hit is { } hit && remote == _remote)
                {
                    remote.Add(ToPaletteHit(hit));
                    Show(query);
                    StateHasChanged();
                }
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or ApiException or HttpRequestException or System.Text.Json.JsonException)
        {
        }
        finally
        {
            if (remote == _remote)
            {
                _streaming = false;
                StateHasChanged();
            }
        }
    }

    private void Show(string query)
    {
        var selected = _active < _hits.Count ? _hits[_active].Item : null;
        var all = _local.Concat(_remote);
        if (_remote.Count > 0)
        {
            all = all.Append(new PaletteHit(new CommandPaletteItem("Search", L["Show all results for “{0}”", query])
            {
                Icon = Icons.Material.Outlined.ManageSearch,
                Href = $"/search?q={Uri.EscapeDataString(query)}",
                Matched = true,
                Order = AllResultsOrder,
            }, 0, []));
        }

        _hits = Rank(all);
        _active = selected is null ? 0 : Math.Max(0, _hits.FindIndex(h => h.Item == selected));
    }

    internal static PaletteHit ToPaletteHit(GlobalSearchHit hit) =>
        new(new CommandPaletteItem(hit.Group, hit.Title)
        {
            Icon = SearchIcons.Resolve(hit.Icon),
            Subtitle = hit.Subtitle,
            Href = hit.Url,
            Matched = true,
            Order = RecordsOrder,
        }, (int)(hit.Score * 1000), [.. hit.Highlights.SelectMany(h => Enumerable.Range(h.Start, Math.Max(0, Math.Min(h.Length, hit.Title.Length - h.Start))))]);

    /// <summary>Recent first, then groups by order and best hit; within a group the best hits, at most eight.</summary>
    internal static List<PaletteHit> Rank(IEnumerable<CommandPaletteItem> items, string query) => Rank(items.Select(i => Score(i, query)).OfType<PaletteHit>());

    internal static List<PaletteHit> Rank(IEnumerable<PaletteHit> hits) =>
        hits.GroupBy(h => h.Item.Group)
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
