using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.Data;

public sealed class FacetSelection
{
    private readonly Dictionary<string, Dictionary<string, SelectedFacet>> _selected = new(StringComparer.Ordinal);

    public event Action? Changed;

    public bool IsEmpty => _selected.Count == 0;

    public IEnumerable<SelectedFacet> All => _selected.Values.SelectMany(g => g.Values);

    public bool IsSelected(FacetGroupDto group, FacetOptionDto option) =>
        _selected.TryGetValue(group.Key, out var options) && options.ContainsKey(option.OData);

    public void Toggle(FacetGroupDto group, FacetOptionDto option)
    {
        if (!_selected.TryGetValue(group.Key, out var options))
        {
            options = new Dictionary<string, SelectedFacet>(StringComparer.Ordinal);
            _selected[group.Key] = options;
        }

        if (!options.Remove(option.OData))
        {
            if (!group.MultiSelect)
            {
                options.Clear();
            }

            options[option.OData] = new SelectedFacet(group.Key, group.Label, group.GroupOperator, option.Label, option.OData);
        }

        if (options.Count == 0)
        {
            _selected.Remove(group.Key);
        }

        Changed?.Invoke();
    }

    public void Remove(SelectedFacet facet)
    {
        if (_selected.TryGetValue(facet.GroupKey, out var options) && options.Remove(facet.OData) && options.Count == 0)
        {
            _selected.Remove(facet.GroupKey);
        }

        Changed?.Invoke();
    }

    public void Clear()
    {
        _selected.Clear();
        Changed?.Invoke();
    }

    public string? ToFilter()
    {
        var groups = _selected.Values
            .Where(g => g.Count > 0)
            .Select(g => g.Values.ToList())
            .Select(options => options.Count == 1
                ? options[0].OData
                : "(" + string.Join(options[0].Operator == FacetOperator.And ? " and " : " or ", options.Select(o => o.OData)) + ")")
            .ToList();
        return groups.Count == 0 ? null : string.Join(" and ", groups);
    }
}

public sealed record SelectedFacet(string GroupKey, string GroupLabel, FacetOperator Operator, string Label, string OData);
