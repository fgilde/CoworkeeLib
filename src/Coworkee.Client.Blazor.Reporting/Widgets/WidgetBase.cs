using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Reporting.Api;
using Coworkee.Client.Blazor.Reporting.State;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Reporting.Widgets;

/// <summary>Loads a widget's data whenever its query, the report's filters or a refresh changed it; reports what it shows for export.</summary>
public abstract class WidgetBase : Coworkee.Client.Blazor.Components.CoworkeeComponentBase, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private string? _loaded;
    private CancellationTokenSource? _loading;

    [Inject] protected IReportsApi Api { get; set; } = null!;

    [Inject] protected CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public ReportWidget Widget { get; set; } = null!;

    [Parameter, EditorRequired] public ReportState State { get; set; } = null!;

    /// <summary>The rows the widget shows, for CSV and Excel export.</summary>
    [Parameter] public EventCallback<ReportSheetRequest> OnSheet { get; set; }

    /// <summary>A short note on the last load, e.g. "12 groups · 18 ms".</summary>
    [Parameter] public EventCallback<string> OnInfo { get; set; }

    protected bool Loading { get; private set; }

    protected string? Error { get; private set; }

    protected WidgetQuery? Query => Widget.Query is { EntitySet.Length: > 0 } query ? query : null;

    protected ReportSourceDto? Source => State.Source(Query?.EntitySet);

    protected string? Filter => Query is { } query ? State.FilterFor(query, Widget.Id) : null;

    protected ReportFieldDto? FieldOf(string? path) => State.Field(Query?.EntitySet, path);

    /// <summary>What decides whether to load again.</summary>
    protected virtual string LoadKey => $"{JsonSerializer.Serialize(Widget.Query, Json)}|{JsonSerializer.Serialize(Widget.Options, Json)}|{Filter}|{State.Version}";

    protected override void OnInitialized() => State.Changed += StateChanged;

    protected override Task OnParametersSetAsync() => LoadIfChangedAsync();

    protected abstract Task LoadAsync(CancellationToken cancellationToken);

    protected Task<ReportQueryResult> RunAsync(ReportQueryRequest request, CancellationToken cancellationToken) => Api.QueryAsync(request, cancellationToken);

    protected Task ReportSheetAsync(IReadOnlyList<string> headers, IEnumerable<IEnumerable<object?>> rows) =>
        OnSheet.InvokeAsync(new ReportSheetRequest(Widget.Title ?? Widget.Type.ToString(), headers,
            [.. rows.Select(r => (IReadOnlyList<JsonElement>)[.. r.Select(c => JsonSerializer.SerializeToElement(c, Json))])]));

    protected Task ReportInfoAsync(string info) => OnInfo.InvokeAsync(info);

    protected string AggregateLabel(ReportAggregate aggregate) => ReportFormat.AggregateLabel(aggregate, FieldOf(aggregate.Field), L);

    public void Dispose()
    {
        Dispose(true);
        State.Changed -= StateChanged;
        _loading?.Cancel();
        _loading?.Dispose();
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }

    private void StateChanged() => InvokeAsync(async () =>
    {
        await LoadIfChangedAsync();
        StateHasChanged();
    });

    private async Task LoadIfChangedAsync()
    {
        var key = LoadKey;
        if (key == _loaded)
        {
            return;
        }

        _loaded = key;
        _loading?.Cancel();
        _loading = new CancellationTokenSource();
        var token = _loading.Token;
        Loading = true;
        Error = null;
        try
        {
            await LoadAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (ApiException exception)
        {
            Error = exception.Errors is { Count: > 0 } errors ? string.Join(" ", errors.SelectMany(e => e.Value)) : L[exception.Status == 403 ? "You may not read this data." : "The data could not be loaded."];
        }
        catch (HttpRequestException)
        {
            Error = L["The data could not be loaded."];
        }

        if (!token.IsCancellationRequested)
        {
            Loading = false;
        }
    }
}
