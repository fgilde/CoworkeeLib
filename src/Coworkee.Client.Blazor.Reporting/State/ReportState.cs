using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Reporting;

namespace Coworkee.Client.Blazor.Reporting.State;

/// <summary>What narrows the widgets of an open report: its facets per entity set, the date range and cross filters from clicks.</summary>
public sealed class ReportState
{
    private readonly Dictionary<string, FacetSelection> _facets = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CrossFilter> _cross = [];
    private readonly TimeProvider _clock;

    public ReportState(ReportDefinition definition, IReadOnlyList<ReportSourceDto> sources, TimeProvider? clock = null)
    {
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
            selection.Restore(definition.Filters.Where(f => string.Equals(f.EntitySet, set, StringComparison.OrdinalIgnoreCase))
                .Select(f => new SelectedFacet(f.GroupKey, f.GroupLabel, f.Operator, f.Label, f.OData)));
            selection.Changed += Store;
        }

        Changed?.Invoke();
    }

    public FacetSelection Facets(string entitySet)
    {
        if (!_facets.TryGetValue(entitySet, out var selection))
        {
            selection = new FacetSelection();
            selection.Restore(Definition.Filters.Where(f => string.Equals(f.EntitySet, entitySet, StringComparison.OrdinalIgnoreCase))
                .Select(f => new SelectedFacet(f.GroupKey, f.GroupLabel, f.Operator, f.Label, f.OData)));
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
        var cross = _cross.Where(c => c.WidgetId != widgetId && string.Equals(c.EntitySet, query.EntitySet, StringComparison.OrdinalIgnoreCase)).Select(c => c.Filter);
        return ODataFilter.And([query.Filter, Selection(query.Facets).ToFilter(), facets, date, .. cross]);
    }

    public static FacetSelection Selection(IEnumerable<ReportFilterValue> values)
    {
        var selection = new FacetSelection();
        selection.Restore(values.Select(f => new SelectedFacet(f.GroupKey, f.GroupLabel, f.Operator, f.Label, f.OData)));
        return selection;
    }

    private void Store()
    {
        Definition.Filters =
        [
            .. _facets.SelectMany(s => s.Value.All.Select(f => new ReportFilterValue(s.Key, f.GroupKey, f.GroupLabel, f.Label, f.OData, f.Operator))),
            .. Definition.Filters.Where(f => !_facets.ContainsKey(f.EntitySet)),
        ];
        Changed?.Invoke();
    }
}

/// <summary>A click on a category of one widget that narrows the other widgets of its entity set.</summary>
public sealed record CrossFilter(string WidgetId, string EntitySet, string Label, string Filter);
