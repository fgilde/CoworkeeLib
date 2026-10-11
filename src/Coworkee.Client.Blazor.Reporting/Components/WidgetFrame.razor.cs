using System.Globalization;
using System.Text;
using System.Text.Json;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Reporting.Api;
using Coworkee.Client.Blazor.Reporting.State;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Reporting.Components;

/// <summary>A widget's card: title, view switch, info, full screen, drill-down, export and, while editing, its settings.</summary>
public partial class WidgetFrame
{
    private ReportSheetRequest? _sheet;
    private string? _info;
    private bool _full;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private IReportsApi Api { get; set; } = null!;

    [Inject] private FileDownloader Downloader { get; set; } = null!;

    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Inject] private ReportingOptions Options { get; set; } = null!;

    [Parameter, EditorRequired] public ReportWidget Widget { get; set; } = null!;

    [Parameter, EditorRequired] public ReportState State { get; set; } = null!;

    [Parameter] public bool Editing { get; set; }

    [Parameter] public bool Selected { get; set; }

    [Parameter] public EventCallback OnSettings { get; set; }

    [Parameter] public EventCallback OnRemove { get; set; }

    [Parameter] public EventCallback OnDuplicate { get; set; }

    [Parameter] public EventCallback OnChanged { get; set; }

    private string Title => Widget.Title is { Length: > 0 } title ? title : L[ReportFormat.Humanize(Widget.Type.ToString())];

    private Task ViewChanged(TableView view)
    {
        Widget.Options.View = view;
        return OnChanged.InvokeAsync();
    }

    private void DrillDown()
    {
        var query = Widget.Query!;
        var facets = State.FilterFor(query, Widget.Id) is { Length: > 0 } filter
            ? [new SelectedFacet("report", L["Report"], FacetOperator.And, Title, filter)]
            : Array.Empty<SelectedFacet>();
        var state = new DataTableState(null, facets, []).Encode();
        Nav.NavigateTo($"{Options.PageFor(query.EntitySet).TrimStart('/')}?{Uri.EscapeDataString(query.EntitySet.ToLowerInvariant())}={Uri.EscapeDataString(state)}");
    }

    private Task ExportCsvAsync()
    {
        var csv = new StringBuilder();
        csv.AppendLine(string.Join(';', _sheet!.Headers.Select(Escape)));
        foreach (var row in _sheet.Rows)
        {
            csv.AppendLine(string.Join(';', row.Select(c => Escape(Cell(c)))));
        }

        return Downloader.DownloadAsync($"{FileName}.csv", "text/csv", csv.ToString());
    }

    private async Task ExportExcelAsync() =>
        await Downloader.DownloadAsync($"{FileName}.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", await Api.SheetAsync(_sheet!));

    private string FileName => string.Concat(Title.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));

    private static string Cell(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.GetDouble().ToString(CultureInfo.CurrentCulture),
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => string.Empty,
    };

    private static string Escape(string value) => value.IndexOfAny([';', '"', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
}
