using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Reporting.Components;

public partial class GroupByEditor
{
    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter] public string Label { get; set; } = string.Empty;

    [Parameter] public IReadOnlyList<ReportFieldDto> Fields { get; set; } = [];

    [Parameter] public ReportGroupBy? Value { get; set; }

    [Parameter] public EventCallback<ReportGroupBy?> ValueChanged { get; set; }

    [Parameter] public bool Optional { get; set; }

    private Task FieldChanged(string? path) =>
        ValueChanged.InvokeAsync(path is null ? null
            : new ReportGroupBy(path, Fields.FirstOrDefault(f => f.Path == path)?.Kind == ReportFieldKind.Date ? DateBucket.Month : DateBucket.None));
}
