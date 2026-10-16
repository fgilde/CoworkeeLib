using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Search;
using Coworkee.Contracts.Search;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Pages;

/// <summary>All hits of the global search for <c>?q=</c>, as they arrive, with a filter per type (<c>?type=</c>).</summary>
public partial class SearchResults : IDisposable
{
    private const int Limit = 20;

    private readonly List<GlobalSearchHit> _hits = [];
    private IReadOnlyList<GlobalSearchProviderDto> _providers = [];
    private string _text = string.Empty;
    private string? _searched;
    private bool _streaming;
    private CancellationTokenSource? _search;

    [SupplyParameterFromQuery(Name = "q")] public string? Q { get; set; }

    [SupplyParameterFromQuery(Name = "type")] public string? Type { get; set; }

    [Inject] private IGlobalSearchApi Api { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private Navigation.CoworkeePages Links { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    private List<GlobalSearchHit> Shown => Type is null ? _hits : [.. _hits.Where(h => h.Provider == Type)];

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _providers = await Api.GetProvidersAsync();
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException)
        {
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        _text = Q ?? string.Empty;
        if (_text != _searched)
        {
            _searched = _text;
            await SearchAsync();
        }
    }

    private Task TextChangedAsync(string? text)
    {
        Nav.NavigateTo(Nav.GetUriWithQueryParameter("q", string.IsNullOrWhiteSpace(text) ? null : text), replace: true);
        return Task.CompletedTask;
    }

    private Task FilterAsync(string? type)
    {
        Nav.NavigateTo(Nav.GetUriWithQueryParameter("type", type), replace: true);
        return Task.CompletedTask;
    }

    private void Open(GlobalSearchHit hit)
    {
        if (hit.Url is { } url)
        {
            Nav.NavigateTo(Links.Href(url) ?? url);
        }
    }

    // ponytail: all types stream at once and the chips filter in the browser; a type filter on the server if providers get many
    private async Task SearchAsync()
    {
        _search?.Cancel();
        var search = _search = new CancellationTokenSource();
        _hits.Clear();
        if (_text.Trim().Length == 0)
        {
            return;
        }

        _streaming = true;
        try
        {
            await foreach (var found in Api.StreamAsync(_text, limit: Limit, cancellationToken: search.Token))
            {
                if (found.Hit is { } hit)
                {
                    _hits.Add(hit);
                    StateHasChanged();
                }
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or ApiException or HttpRequestException or System.Text.Json.JsonException)
        {
        }
        finally
        {
            if (search == _search)
            {
                _streaming = false;
            }
        }
    }

    public void Dispose()
    {
        _search?.Cancel();
        _search?.Dispose();
    }
}
