using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Reporting.Api;
using Coworkee.Client.Blazor.Reporting.Widgets;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Reporting.Pages;

/// <summary>Drill-down for entity sets without a page of their own: the library's data table over any set.</summary>
public partial class ReportData
{
    private ReportSourceDto? _source;
    private DynamicRowSource? _rows;
    private bool _missing;

    [Inject] private IReportsApi Api { get; set; } = null!;

    [Inject] private IODataClient OData { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public string EntitySet { get; set; } = string.Empty;

    private IReadOnlyList<ReportFieldDto> Columns => [.. (_source?.Fields ?? []).Where(f => f.Kind != ReportFieldKind.Guid).Take(DynamicRow.MaxColumns)];

    private IReadOnlyCollection<string> SearchFields => [.. Columns.Where(c => c.Kind == ReportFieldKind.Text && !c.Path.Contains('/')).Select(c => c.Path)];

    protected override async Task OnParametersSetAsync()
    {
        _source = (await Api.GetSourcesAsync()).FirstOrDefault(s => string.Equals(s.EntitySet, EntitySet, StringComparison.OrdinalIgnoreCase));
        _missing = _source is null;
        _rows = _source is null ? null : new DynamicRowSource(OData, _source.EntitySet, [.. Columns.Select(c => c.Path)]);
    }
}
