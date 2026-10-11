using System.Text.Json.Serialization;
using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.Data;

/// <summary>
/// The chosen facet values of a table. Within a group chosen values join with "or" (any of) or "and" (all of), excluded values with
/// "not"; groups join with "and". A group can also hold one custom value, such as a range or a date preset.
/// </summary>
public sealed class FacetSelection
{
    private const string Custom = "\u0001custom";
    private readonly Dictionary<string, Dictionary<string, SelectedFacet>> _selected = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FacetCombine> _combine = new(StringComparer.Ordinal);

    public event Action? Changed;

    public bool IsEmpty => _selected.Count == 0;

    public IEnumerable<SelectedFacet> All => _selected.Values.SelectMany(g => g.Values);

    /// <summary>"Now" for date presets; tests set it.</summary>
    public Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.Now;

    public bool IsSelected(FacetGroupDto group, FacetOptionDto option) => Find(group, option) is { Exclude: false };

    public bool IsExcluded(FacetGroupDto group, FacetOptionDto option) => Find(group, option) is { Exclude: true };

    public IEnumerable<SelectedFacet> Of(string groupKey) => _selected.TryGetValue(groupKey, out var options) ? options.Values : [];

    /// <summary>How the group combines its values: the user's switch, else the page's or the server's setting.</summary>
    public FacetCombine CombineOf(FacetGroupDto group) => _combine.TryGetValue(group.Key, out var combine) ? combine : Facets.Combine(group);

    /// <summary>Chooses or unchooses an option; with <paramref name="exclude"/> it excludes it instead. Single groups keep one chosen value.</summary>
    public void Toggle(FacetGroupDto group, FacetOptionDto option, bool exclude = false)
    {
        var options = Group(group.Key);
        if (options.TryGetValue(option.OData, out var known) && known.Exclude == exclude)
        {
            options.Remove(option.OData);
        }
        else
        {
            if (!exclude && CombineOf(group) == FacetCombine.Single)
            {
                foreach (var chosen in options.Values.Where(o => !o.Exclude).ToList())
                {
                    options.Remove(chosen.OData);
                }
            }

            options.Remove(Custom);
            options[option.OData] = new SelectedFacet(group.Key, group.Label, Operator(CombineOf(group)), option.Label, option.OData) { Exclude = exclude };
        }

        Tidy(group.Key);
        Changed?.Invoke();
    }

    /// <summary>Replaces the group's choice with one custom value, e.g. "10 – 50" or the preset "last 7 days"; <c>null</c> clears the group.</summary>
    public void Set(FacetGroupDto group, string? label, string? odata, string? preset = null)
    {
        _selected.Remove(group.Key);
        if (label is not null && odata is not null)
        {
            Group(group.Key)[Custom] = new SelectedFacet(group.Key, group.Label, FacetOperator.Or, label, odata) { Preset = preset, Path = group.ValuePath ?? group.Field };
        }

        Changed?.Invoke();
    }

    public void SetCombine(FacetGroupDto group, FacetCombine combine)
    {
        _combine[group.Key] = combine;
        if (_selected.TryGetValue(group.Key, out var options))
        {
            foreach (var (key, facet) in options.ToList())
            {
                options[key] = facet with { Operator = Operator(combine) };
            }

            foreach (var extra in combine == FacetCombine.Single ? options.Where(o => !o.Value.Exclude).Skip(1).ToList() : [])
            {
                options.Remove(extra.Key);
            }
        }

        Changed?.Invoke();
    }

    public void Remove(SelectedFacet facet)
    {
        if (_selected.TryGetValue(facet.GroupKey, out var options) && options.FirstOrDefault(o => o.Value == facet).Key is { } key)
        {
            options.Remove(key);
            Tidy(facet.GroupKey);
        }

        Changed?.Invoke();
    }

    /// <summary>Replaces the selection, e.g. from the URL or a saved view.</summary>
    public void Restore(IEnumerable<SelectedFacet> facets)
    {
        _selected.Clear();
        _combine.Clear();
        foreach (var facet in facets)
        {
            Group(facet.GroupKey)[facet.Path is not null ? Custom : facet.OData] = facet;
            if (facet.Operator == FacetOperator.And)
            {
                _combine[facet.GroupKey] = FacetCombine.All;
            }
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
        var groups = _selected.Keys.Select(GroupFilter).OfType<string>().ToList();
        return groups.Count == 0 ? null : string.Join(" and ", groups);
    }

    /// <summary>The filter of one group: chosen values with its operator, excluded values with "not".</summary>
    public string? GroupFilter(string groupKey)
    {
        if (!_selected.TryGetValue(groupKey, out var options) || options.Count == 0)
        {
            return null;
        }

        var chosen = options.Values.Where(o => !o.Exclude).Select(ODataOf).ToList();
        var excluded = options.Values.Where(o => o.Exclude).Select(ODataOf).ToList();
        var join = options.Values.First().Operator == FacetOperator.And ? " and " : " or ";
        var include = chosen.Count switch
        {
            0 => null,
            1 => chosen[0],
            _ => "(" + string.Join(join, chosen) + ")",
        };
        var exclude = excluded.Count switch
        {
            0 => null,
            1 => $"not ({excluded[0]})",
            _ => "not (" + string.Join(" or ", excluded) + ")",
        };
        return include is not null && exclude is not null ? $"({include} and {exclude})" : include ?? exclude;
    }

    /// <summary>The selection apart from <paramref name="baseFilter"/>, so the server counts each "any of" group without its own choice.</summary>
    public FacetContextDto? Context(string? baseFilter, IReadOnlyList<FacetGroupDto> groups)
    {
        var filters = _selected.Keys.Select(k => (Key: k, Filter: GroupFilter(k))).Where(g => g.Filter is not null).ToDictionary(g => g.Key, g => g.Filter!);
        if (filters.Count == 0)
        {
            return null;
        }

        var combines = filters.Keys.ToDictionary(k => k, k => groups.FirstOrDefault(g => g.Key == k) is { } group ? CombineOf(group) : FacetCombine.Any);
        return new FacetContextDto(baseFilter, filters, combines);
    }

    private string ODataOf(SelectedFacet facet) =>
        facet is { Preset: { } preset, Path: { } path } && FacetPresets.ODataFor(preset, path, Now()) is { } fresh ? fresh : facet.OData;

    private SelectedFacet? Find(FacetGroupDto group, FacetOptionDto option) =>
        _selected.TryGetValue(group.Key, out var options) && options.TryGetValue(option.OData, out var facet) ? facet : null;

    private Dictionary<string, SelectedFacet> Group(string key)
    {
        if (!_selected.TryGetValue(key, out var options))
        {
            options = new Dictionary<string, SelectedFacet>(StringComparer.Ordinal);
            _selected[key] = options;
        }

        return options;
    }

    private void Tidy(string key)
    {
        if (_selected.TryGetValue(key, out var options) && options.Count == 0)
        {
            _selected.Remove(key);
        }
    }

    private static FacetOperator Operator(FacetCombine combine) => combine == FacetCombine.All ? FacetOperator.And : FacetOperator.Or;
}

/// <summary>A chosen facet value as URLs and saved views keep it. <see cref="Preset"/> values recompute their range each time, so "today" stays today.</summary>
public sealed record SelectedFacet(string GroupKey, string GroupLabel, FacetOperator Operator, string Label, string OData)
{
    /// <summary>The value is excluded: rows without it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Exclude { get; init; }

    /// <summary>A date preset such as <c>last7</c>; see <see cref="FacetPresets"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Preset { get; init; }

    /// <summary>The OData path of a custom value (a range or a preset); <c>null</c> for an option of the facet.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; init; }
}
