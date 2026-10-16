using System.Text.Json;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Reporting.Api;
using Coworkee.Client.Blazor.Reporting.Components;
using Coworkee.Client.Blazor.Reporting.State;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;

namespace Coworkee.Client.Blazor.Reporting.Pages;

/// <summary>Shows a report and, for its owner, edits it: widgets are dragged and resized on the grid and configured in the side panel.</summary>
public partial class ReportPage : IAsyncDisposable
{
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private ReportDto? _report;
    private DashboardReportDto? _dashboard;
    private ReportDefinition? _definition;
    private ReportState? _state;
    private ReportGrid? _grid;
    private ElementReference _board;
    private ReportWidget? _selected;
    private string? _snapshot;
    private string? _failed;
    private bool _editing;
    private bool _saving;
    private CancellationTokenSource? _timer;
    private CancellationTokenSource? _debounce;

    [Inject] private IReportsApi Api { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private FileDownloader Downloader { get; set; } = null!;

    [Inject] private IJSRuntime JS { get; set; } = null!;

    [Inject] private ReportTheme Theme { get; set; } = null!;

    [Parameter] public Guid Id { get; set; }

    [SupplyParameterFromQuery(Name = "edit")] public bool Edit { get; set; }

    private IEnumerable<string> DataTopics => _state is null ? [] : _state.EntitySets.Select(s => _state.Source(s)?.Topic).OfType<string>().Distinct();

    protected override async Task OnParametersSetAsync()
    {
        try
        {
            var sources = Api.GetSourcesAsync();
            _report = await Api.GetReportAsync(Id);
            _definition = Clone(_report.Definition);
            _state = new ReportState(_definition, await sources);
            _editing = Edit && _report.CanEdit;
            _snapshot = Serialize();
            StartTimer();
        }
        catch (ApiException exception)
        {
            _failed = exception.Status == 404 ? L["The report does not exist or is not shared with you."] : L["The report could not be loaded."];
            return;
        }

        try
        {
            _dashboard = await Api.GetDashboardAsync();
        }
        catch (ApiException)
        {
        }
    }

    private void StartEdit()
    {
        _editing = true;
        _undo.Clear();
        _redo.Clear();
        _snapshot = Serialize();
    }

    private void CancelEdit()
    {
        _editing = false;
        _selected = null;
        Load(Clone(_report!.Definition));
    }

    private async Task SaveAsync()
    {
        _saving = true;
        try
        {
            _report = await Api.UpdateAsync(_report!.Id, new SaveReportRequest(_report.Name, _report.Description, _report.Icon, _report.Visibility, _report.RoleIds, _definition!));
            _editing = false;
            _selected = null;
            StartTimer();
            Snackbar.Add(L["Report saved."], Severity.Success);
        }
        catch (ApiException exception)
        {
            Snackbar.Add(exception.Errors is { Count: > 0 } errors ? string.Join(" ", errors.SelectMany(e => e.Value)) : L["The report could not be saved."], Severity.Error);
        }
        finally
        {
            _saving = false;
        }
    }

    private async Task SettingsAsync()
    {
        var settings = await ReportSettingsDialog.ShowAsync(Dialogs, L["Report settings"], new ReportSettings
        {
            Name = _report!.Name,
            Description = _report.Description,
            Icon = _report.Icon,
            RefreshSeconds = _definition!.RefreshSeconds,
            Visibility = _report.Visibility,
            RoleIds = [.. _report.RoleIds],
        });
        if (settings is not null)
        {
            Remember();
            _definition.RefreshSeconds = settings.RefreshSeconds;
            _report = _report with { Name = settings.Name, Description = settings.Description, Icon = settings.Icon, Visibility = settings.Visibility, RoleIds = settings.RoleIds };
        }
    }

    private void Add(WidgetPreset preset)
    {
        Remember();
        var widget = WidgetPresets.Create(preset, _state!.Sources, _definition!.Widgets, _state.EntitySets.FirstOrDefault());
        widget.Title = L[preset.Name];
        _definition.Widgets.Add(widget);
        _selected = widget;
    }

    private void Remove(ReportWidget widget)
    {
        Remember();
        _definition!.Widgets.Remove(widget);
        if (_selected == widget)
        {
            _selected = null;
        }
    }

    private void Duplicate(ReportWidget widget)
    {
        Remember();
        var copy = Clone(widget);
        copy.Id = new ReportWidget().Id;
        copy.Y = widget.Y + widget.H;
        _definition!.Widgets.Add(copy);
        _selected = copy;
    }

    private void Select(ReportWidget widget)
    {
        if (_selected != widget)
        {
            Remember();
            _selected = widget;
        }
    }

    // settings change one field at a time: the history keeps the state from before the panel opened
    private void SettingsChanged() => _snapshot = Serialize();

    private void LayoutChanged() => Remember();

    private void Remember()
    {
        if (_snapshot is not null)
        {
            _undo.Push(_snapshot);
        }

        _redo.Clear();
        _snapshot = Serialize();
    }

    private void Undo() => Restore(_undo, _redo);

    private void Redo() => Restore(_redo, _undo);

    private void Restore(Stack<string> from, Stack<string> to)
    {
        if (from.Count == 0)
        {
            return;
        }

        to.Push(Serialize());
        var definition = JsonSerializer.Deserialize<ReportDefinition>(from.Pop(), Reports.Json)!;
        _selected = null;
        Load(definition);
        _snapshot = Serialize();
    }

    private void Load(ReportDefinition definition)
    {
        _definition = definition;
        _state!.Load(definition);
    }

    private async Task ReportChangedAsync()
    {
        if (_editing || _saving)
        {
            return;
        }

        try
        {
            var report = await Api.GetReportAsync(Id);
            if (report.ModifiedAt != _report?.ModifiedAt)
            {
                _report = report;
                Load(Clone(report.Definition));
                StartTimer();
                Snackbar.Add(L["The report was changed and is up to date again."], Severity.Info);
            }
        }
        catch (ApiException)
        {
            Nav.NavigateTo("reports");
        }
    }

    // many rows change at once in imports: wait for the burst to end
    private void DataChanged()
    {
        _debounce?.Cancel();
        _debounce = new CancellationTokenSource();
        var token = _debounce.Token;
        _ = Task.Delay(1500, token).ContinueWith(_ => InvokeAsync(() => _state?.Refresh()), token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    private void StartTimer()
    {
        _timer?.Cancel();
        if (_definition is not { RefreshSeconds: > 0 } definition)
        {
            return;
        }

        _timer = new CancellationTokenSource();
        var token = _timer.Token;
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(definition.RefreshSeconds));
            while (await timer.WaitForNextTickAsync(token))
            {
                if (!_editing)
                {
                    await InvokeAsync(() => _state?.Refresh());
                }
            }
        }, token);
    }

    private async Task FavoriteAsync()
    {
        await Api.SetFavoriteAsync(_report!.Id, !_report.IsFavorite);
        _report = _report with { IsFavorite = !_report.IsFavorite };
    }

    private async Task DuplicateAsync()
    {
        var copy = await Api.DuplicateAsync(_report!.Id);
        Nav.NavigateTo($"reports/{copy.Id}?edit=true");
    }

    private async Task PngAsync()
    {
        await using var module = await JS.InvokeAsync<IJSObjectReference>("import", ReportGrid.Module);
        await module.InvokeVoidAsync("png", _board, $"{_report!.Name}.png", (await Theme.GetAsync()).Surface);
    }

    private async Task PrintAsync()
    {
        await using var module = await JS.InvokeAsync<IJSObjectReference>("import", ReportGrid.Module);
        await module.InvokeVoidAsync("print");
    }

    private async Task PresentAsync()
    {
        await using var module = await JS.InvokeAsync<IJSObjectReference>("import", ReportGrid.Module);
        await module.InvokeVoidAsync("fullscreen", _board);
    }

    private Task ExportJsonAsync() =>
        Downloader.DownloadAsync(_report!.Name + ReportFile.Extension, "application/json",
            JsonSerializer.Serialize(new ReportFile(_report.Name, _report.Description, _report.Icon, _definition!), Reports.Json));

    private string Serialize() => JsonSerializer.Serialize(_definition, Reports.Json);

    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Reports.Json), Reports.Json)!;

    public ValueTask DisposeAsync()
    {
        _timer?.Cancel();
        _debounce?.Cancel();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
