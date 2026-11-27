using Coworkee.Client.Blazor.Data;
using Microsoft.Extensions.Logging;
using Coworkee.Contracts.Reporting;

namespace Coworkee.Client.Blazor.Reporting.State;

/// <summary>What narrows the widgets of an open report: its facets per entity set, the date range and cross filters from clicks.</summary>
public sealed class ReportState
{
    private readonly Dictionary<string, FacetSelection> _facets = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CrossFilter> _cross = [];
    private readonly TimeProvider _clock;

    private readonly ILogger? _log;
    private readonly HashSet<string> _logged = new(StringComparer.Ordinal);

    public ReportState(ReportDefinition definition, IReadOnlyList<ReportSourceDto> sources, TimeProvider? clock = null, ILogger<ReportState>? log = null)
    {
        _log = log;
        Definition = definition;
        Sources = sources;
        _clock = clock ?? TimeProvider.System;
    }

    public event Action? Changed;

    public ReportDefinition Definition { get; private set; }

    public IReadOnlyList<ReportSourceDto> Sources { get; }

    public IReadOnlyList<CrossFilter> CrossFilters => _cross;

    /// <summary>Grows with every refresh, so widgets reload although their filter stayed the same.</summary>
    public int Version { get; private set; }

    public ReportDateRange? DateRange
    {
        get => Definition.DateRange;
        set
        {
            Definition.DateRange = value;
            Changed?.Invoke();
        }
    }

    private string? _dateField;

    /// <summary>The report's date field the widgets are designed with (the first of <see cref="ReportDefinition.DateFields"/>), null without a choice.</summary>
    public string? DefaultDateField => Definition.DateFields.Count > 1 ? Definition.DateFields[0] : null;

    /// <summary>The report's <see cref="ReportDefinition.DateFields"/> with their labels, when there is a choice (two or more).</summary>
    public IReadOnlyList<ReportFieldDto> DateFields => DefaultDateField is null ? []
        : [.. Definition.DateFields.Select(f => Sources.SelectMany(s => s.Fields).FirstOrDefault(x => string.Equals(x.Path, f, StringComparison.OrdinalIgnoreCase)) ?? new ReportFieldDto(f, f, ReportFieldKind.Date))];

    /// <summary>The date field the viewer chose; null for the default (also when the report no longer offers the choice).</summary>
    public string? DateField
    {
        get => DefaultDateField is { } first && _dateField is { } chosen && !string.Equals(chosen, first, StringComparison.OrdinalIgnoreCase)
            ? Definition.DateFields.FirstOrDefault(f => string.Equals(f, chosen, StringComparison.OrdinalIgnoreCase)) : null;
        set
        {
            if (!string.Equals(_dateField, value, StringComparison.OrdinalIgnoreCase))
            {
                _dateField = value;
                Changed?.Invoke();
            }
        }
    }

    public (DateOnly From, DateOnly To)? Range => DateRange is { } range ? DateRanges.Resolve(range, DateOnly.FromDateTime(_clock.GetLocalNow().DateTime)) : null;

    public IEnumerable<string> EntitySets => Definition.Widgets.Select(w => w.Query?.EntitySet).OfType<string>().Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);

    public ReportSourceDto? Source(string? entitySet) => Sources.FirstOrDefault(s => string.Equals(s.EntitySet, entitySet, StringComparison.OrdinalIgnoreCase));

    public ReportFieldDto? Field(string? entitySet, string? path) =>
        Source(entitySet)?.Fields.FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>A new definition, e.g. after a save elsewhere: facets follow it, cross filters stay.</summary>
    public void Load(ReportDefinition definition)
    {
        Definition = definition;
        foreach (var (set, selection) in _facets)
        {
            selection.Changed -= Store;
            selection.Restore(definition.Filters.Where(f => string.Equals(f.EntitySet, set, StringComparison.OrdinalIgnoreCase)).Select(Facet));
            selection.Changed += Store;
        }

        Changed?.Invoke();
    }

    public FacetSelection Facets(string entitySet)
    {
        if (!_facets.TryGetValue(entitySet, out var selection))
        {
            selection = new FacetSelection();
            selection.Restore(Definition.Filters.Where(f => string.Equals(f.EntitySet, entitySet, StringComparison.OrdinalIgnoreCase)).Select(Facet));
            selection.Changed += Store;
            _facets[entitySet] = selection;
        }

        return selection;
    }

    public void ToggleCross(CrossFilter filter)
    {
        var existing = _cross.FindIndex(c => c.WidgetId == filter.WidgetId);
        if (existing >= 0 && _cross[existing].Filter == filter.Filter)
        {
            _cross.RemoveAt(existing);
        }
        else if (existing >= 0)
        {
            _cross[existing] = filter;
        }
        else
        {
            _cross.Add(filter);
        }

        Changed?.Invoke();
    }

    public void RemoveCross(CrossFilter filter)
    {
        _cross.Remove(filter);
        Changed?.Invoke();
    }

    public void ClearAll()
    {
        _cross.Clear();
        foreach (var selection in _facets.Values)
        {
            selection.Clear();
        }

        DateRange = null;
    }

    public void Refresh()
    {
        Version++;
        Changed?.Invoke();
    }

    /// <summary>The widget's own filter with the report's facets, date range and the cross filters of other widgets on its set.</summary>
    public string? FilterFor(WidgetQuery query, string? widgetId = null, bool previousPeriod = false)
    {
        var range = Range;
        if (previousPeriod && range is { } current)
        {
            range = DateRanges.Previous(current);
        }

        var date = range is { } r && Field(query.EntitySet, query.DateField) is { } field ? DateRanges.Filter(field.Path, r, field.DateOnly) : null;
        var facets = Facets(query.EntitySet).ToFilter();
        return ODataFilter.And([query.Filter, Selection(query.Facets).ToFilter(), facets, Shared(query.EntitySet), date, .. Cross(query.EntitySet, widgetId), ReportFilters.RequiredDate(query)]);
    }

    /// <summary>
    /// What narrows the facet counts of <paramref name="entitySet"/> besides its own selection: facets chosen on other sets with the same field and the cross filters.
    /// The set's own groups count without their own choice (the facet context); this is the rest of the filter.
    /// </summary>
    public string? ContextFor(string entitySet) => ODataFilter.And([Shared(entitySet), .. Cross(entitySet, null)]);

    // facets chosen on other sets with the same field (same kind) apply as well
    private string? Shared(string entitySet) =>
        Selection(ReportFilters.Applying(Definition.Filters, entitySet, FieldsOf).Where(f => !string.Equals(f.EntitySet, entitySet, StringComparison.OrdinalIgnoreCase))).ToFilter();

    // the cross filters of other widgets on the set or on a set with the field
    private IEnumerable<string> Cross(string entitySet, string? widgetId) =>
        _cross.Where(c => c.WidgetId != widgetId && (string.Equals(c.EntitySet, entitySet, StringComparison.OrdinalIgnoreCase)
            || ReportFilters.Shares(FieldsOf(c.EntitySet), FieldsOf(entitySet), c.Field))).Select(c => c.Filter);

    private IReadOnlyList<ReportFieldDto>? FieldsOf(string entitySet) => Source(entitySet)?.Fields;

    /// <summary>Whether sets with the facet's field may share it: the kind must be the same in both. A clash is logged once.</summary>
    public bool CanShare(string key, string? field, string firstSet, string otherSet)
    {
        // a group without a field is shared by its key, as FilterFor takes it (ReportFilterValue.Path)
        field ??= key;
        var (a, b) = (Field(firstSet, field)?.Kind, Field(otherSet, field)?.Kind);
        if (a is not null && b is not null && a != b && _logged.Add($"{key}|{firstSet}|{otherSet}"))
        {
            _log?.LogInformation("Facet {Key} is a {First} in {FirstSet} and a {Other} in {OtherSet}: not shared", key, a, firstSet, b, otherSet);
        }

        return a is not null && a == b;
    }

    private static SelectedFacet Facet(ReportFilterValue f) => new(f.GroupKey, f.GroupLabel, f.Operator, f.Label, f.OData) { Field = f.Field };

    public static FacetSelection Selection(IEnumerable<ReportFilterValue> values)
    {
        var selection = new FacetSelection();
        selection.Restore(values.Select(Facet));
        return selection;
    }

    private void Store()
    {
        Definition.Filters =
        [
            .. _facets.SelectMany(s => s.Value.All.Select(f => new ReportFilterValue(s.Key, f.GroupKey, f.GroupLabel, f.Label, f.OData, f.Operator, f.Field))),
            .. Definition.Filters.Where(f => !_facets.ContainsKey(f.EntitySet)),
        ];
        Changed?.Invoke();
    }
}
