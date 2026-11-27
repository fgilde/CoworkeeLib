using Coworkee.Contracts.Reporting;

namespace Coworkee.Client.Blazor.Reporting.State;

/// <summary>The fields the report settings offer: date fields for the date choice, every plain field for the filter bar, from the sets the report's widgets use.</summary>
public static class ReportFieldChoices
{
    public static IReadOnlyList<ReportFieldDto> Dates(IEnumerable<ReportSourceDto> sources) => Distinct(sources, f => f.Kind == ReportFieldKind.Date);

    public static IReadOnlyList<ReportFieldDto> Facets(IEnumerable<ReportSourceDto> sources) => Distinct(sources, f => f.Kind != ReportFieldKind.Guid);

    private static IReadOnlyList<ReportFieldDto> Distinct(IEnumerable<ReportSourceDto> sources, Func<ReportFieldDto, bool> where) =>
        [.. sources.SelectMany(s => s.Fields).Where(where).DistinctBy(f => f.Path, StringComparer.OrdinalIgnoreCase)];
}
