using Coworkee.Client.Blazor.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>How a <see cref="CoworkeeDataTable{T}"/> shows its rows.</summary>
public enum DataTableView
{
    /// <summary>Rows and columns.</summary>
    Table,

    /// <summary>One card per row (<c>ItemTemplate</c>).</summary>
    Cards,
}

/// <summary>
/// The rows as cards (<see cref="ItemTemplate"/>, <see cref="View"/>): the table's query (filter, search, facets, sort) in pages of <see cref="PageSize"/>,
/// the next page when the end of the cards scrolls into view. The grid stays hidden behind them, so columns, sort and layout keep their state.
/// </summary>
public partial class CoworkeeDataTable<T>
{
    private readonly List<T> _cards = [];
    private ODataQuery? _cardQuery;
    private bool _cardsLoaded;
    private bool _cardsLoading;
    private bool _cardsWere;
    private ElementReference _cardsEnd;
    private string? _observedEnd;
    private IJSObjectReference? _cardsObserver;
    private DotNetObjectReference<CoworkeeDataTable<T>>? _self;

    /// <summary>A card per row, for <see cref="View"/> <c>Cards</c>.</summary>
    [Parameter] public RenderFragment<T>? ItemTemplate { get; set; }

    /// <summary>Rows (default) or cards; cards need an <see cref="ItemTemplate"/>.</summary>
    [Parameter] public DataTableView View { get; set; }

    private bool CardsShown => View == DataTableView.Cards && ItemTemplate is not null;

    // cards and table switched: the rows load the other way (a switch between pages and scrolling reloads after the render by itself)
    private bool FollowView()
    {
        if (CardsShown == _cardsWere)
        {
            return false;
        }

        _cardsWere = CardsShown;
        // to the cards: they load at once (ReloadAsync); back to the rows: a switch between pages and scrolling reloads after the render by itself
        return _grid is not null && (CardsShown || _wasEndless == Endless);
    }

    // the grid's load in cards mode: the first page of cards; the hidden grid gets no rows
    private async Task<GridData<T>> LoadCardsAsync(IEnumerable<SortDefinition<T>> sorts, CancellationToken cancellationToken)
    {
        _orderBy = OrderBy(sorts);
        NotifySort();
        var filter = CurrentFilter;
        var context = Facets ? _selection.Context(BaseFilter, _facets) : null;
        var query = _cardQuery = new ODataQuery { Filter = filter, OrderBy = _orderBy, Expand = Expand, Top = PageSize, Skip = 0 };
        var page = await QueryAsync(query, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _cards.Clear();
        _cards.AddRange(page.Items);
        _cardsLoaded = true;
        Counted(page.Count ?? page.Items.Count);
        LoadFacets(filter, context);
        await OnLoaded.InvokeAsync(page.Items);
        StateHasChanged();
        return new GridData<T> { Items = [], TotalItems = 0 };
    }

    /// <summary>The next page of cards (the end of the cards scrolled into view, or *Load more*); true while more may come.</summary>
    [JSInvokable]
    public async Task<bool> MoreCardsAsync()
    {
        if (_cardQuery is not { } query || _cardsLoading || _cards.Count >= _count)
        {
            return false;
        }

        _cardsLoading = true;
        try
        {
            var page = await QueryAsync(query with { Skip = _cards.Count, Count = false });
            // a newer query started over meanwhile
            if (!ReferenceEquals(query, _cardQuery))
            {
                return false;
            }

            _cards.AddRange(page.Items);
            await OnLoaded.InvokeAsync(page.Items);
            StateHasChanged();
            return page.Items.Count > 0 && _cards.Count < _count;
        }
        catch (Exception exception) when (exception is Api.ApiException or HttpRequestException)
        {
            Snackbar.Add(L["The data could not be loaded."], Severity.Error);
            return false;
        }
        finally
        {
            _cardsLoading = false;
        }
    }

    private Task CardClickedAsync(T item) => RowOpens ? OpenRowAsync(item) : Task.CompletedTask;

    // watches the end of the cards, so scrolling there loads the next page
    private async Task ObserveCardsEndAsync()
    {
        if (!CardsShown)
        {
            if (_cardsObserver is not null)
            {
                await StopObservingAsync();
            }

            return;
        }

        if (_cardsEnd.Id is null || _cardsEnd.Id == _observedEnd)
        {
            return;
        }

        await StopObservingAsync();
        _observedEnd = _cardsEnd.Id;
        try
        {
            var module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/Coworkee.Client.Blazor/coworkee.js");
            _cardsObserver = await module.InvokeAsync<IJSObjectReference>("observeEnd", _cardsEnd, _self ??= DotNetObjectReference.Create(this), nameof(MoreCardsAsync));
        }
        catch (JSDisconnectedException)
        {
        }
    }

    private async Task StopObservingAsync()
    {
        _observedEnd = null;
        if (_cardsObserver is { } observer)
        {
            _cardsObserver = null;
            try
            {
                await observer.InvokeVoidAsync("dispose");
                await observer.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }
    }

    private void DisposeCards()
    {
        _ = StopObservingAsync();
        _self?.Dispose();
    }
}
