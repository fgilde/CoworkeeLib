using Coworkee.Client.Blazor.Localization;
using Coworkee.Client.Blazor.Reporting.State;
using Coworkee.Contracts.Reporting;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Reporting.Components;

/// <summary>Edits a widget in place, so it redraws while the author changes it; the page keeps the undo history.</summary>
public partial class WidgetSettings
{
    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Parameter, EditorRequired] public ReportWidget Widget { get; set; } = null!;

    [Parameter, EditorRequired] public ReportState State { get; set; } = null!;

    [Parameter] public EventCallback OnChanged { get; set; }

    [Parameter] public EventCallback OnClose { get; set; }

    private WidgetQuery? Query => WidgetPresets.HasData(Widget.Type) ? Widget.Query : null;

    private IReadOnlyList<ReportFieldDto> Fields => State.Source(Widget.Query?.EntitySet)?.Fields ?? [];

    private IEnumerable<ReportFieldDto> NumberFields => Fields.Where(f => f.Kind is ReportFieldKind.Integer or ReportFieldKind.Number);

    private Task Change(Action change)
    {
        change();
        return OnChanged.InvokeAsync();
    }

    private Task SourceChanged(string entitySet)
    {
        var preset = WidgetPresets.All.First(p => p.Type == Widget.Type && (p.Type != WidgetType.Chart || p.Chart == Widget.Options.Chart));
        var fresh = WidgetPresets.Create(preset, State.Sources, [], entitySet);
        return Change(() => Widget.Query = fresh.Query);
    }

    // a grouped table names its columns after their values: a new name moves the order and hidden entries of the old one along
    private Task SetAggregate(int index, ReportAggregate aggregate) => Change(() =>
    {
        var query = Widget.Query!;
        var before = ReportTableColumns.Name(query.Aggregates[index]);
        query.Aggregates[index] = aggregate;
        if (Widget.Type == WidgetType.GroupedTable)
        {
            Rename(before, ReportTableColumns.Name(aggregate));
        }
    });

    private void Rename(string from, string to)
    {
        var query = Widget.Query!;
        if (string.Equals(from, to, StringComparison.Ordinal) || ReportTableColumns.Of(query).Any(c => Same(c.Name, from)))
        {
            return;
        }

        query.OrderBy = ReportTableColumns.OrderBy(ReportTableColumns.Entries(query.OrderBy).Select(e => Same(e.Name, from) ? (to, e.Descending) : e));
        Widget.Options.HiddenColumns = [.. Widget.Options.HiddenColumns.Select(h => Same(h, from) ? to : h)];
    }

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private string AggregateLabel(ReportAggregate aggregate) => ReportFormat.AggregateLabel(aggregate, State.Field(Widget.Query?.EntitySet, aggregate.Field), L);

    // grouped tables: the value's column has a name another column has too
    private bool Clashes(int index) =>
        Widget.Type == WidgetType.GroupedTable && Widget.Query is { } query
        && ReportTableColumns.Of(query).Count(c => Same(c.Name, ReportTableColumns.Name(query.Aggregates[index]))) > 1;

    private Task ColumnsChanged(IEnumerable<string> columns) => Change(() => Widget.Query!.Columns = [.. columns]);

    // grouped tables: group fields, columns by name, the order as entries of WidgetQuery.OrderBy
    private IEnumerable<ReportFieldDto> GroupFieldChoices(string? current) =>
        Fields.Where(f => f.Kind != ReportFieldKind.Guid && (f.Path == current || !Widget.Query!.GroupFields.Contains(f.Path, StringComparer.OrdinalIgnoreCase)));

    private ReportFieldDto? NextGroupField => GroupFieldChoices(null).FirstOrDefault();

    private List<ReportTableColumns.Column> TableColumns => [.. ReportTableColumns.Of(Widget.Query!).DistinctBy(c => c.Name, StringComparer.OrdinalIgnoreCase)];

    private string ColumnLabel(string name) =>
        TableColumns.FirstOrDefault(c => Same(c.Name, name)) is { } column ? ReportFormat.ColumnLabel(column, State, L) : name;

    private string JoinText(WidgetJoin join) =>
        $"{L.Stored(State.Source(join.EntitySet)?.Label ?? join.EntitySet)} · {string.Join(", ", join.Aggregates.Select(a => ReportFormat.AggregateLabel(a, State.Field(join.EntitySet, a.Field), L)))}";

    private List<(string Name, bool Descending)> SortEntries => [.. ReportTableColumns.Entries(Widget.Query!.OrderBy)];

    // the columns not sorted by yet, and the entry's own (also one no longer in the table, so it is not lost unseen)
    private List<string> SortChoices(string? current) =>
        [.. new[] { current }.OfType<string>().Concat(TableColumns.Select(c => c.Name)).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(n => string.Equals(n, current, StringComparison.OrdinalIgnoreCase) || !SortEntries.Exists(e => string.Equals(e.Name, n, StringComparison.OrdinalIgnoreCase)))];

    private Task SetSort(int index, (string Name, bool Descending) entry)
    {
        var entries = SortEntries;
        entries[index] = entry;
        return SetSorts(entries);
    }

    private Task SetSorts(IEnumerable<(string Name, bool Descending)> entries) => Change(() => Widget.Query!.OrderBy = ReportTableColumns.OrderBy(entries));

    private static void Move<T>(List<T> items, int from, int to)
    {
        var item = items[from];
        items.RemoveAt(from);
        items.Insert(to, item);
    }

    private static List<T> Moved<T>(List<T> items, int from, int to)
    {
        Move(items, from, to);
        return items;
    }
}
