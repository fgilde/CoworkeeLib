using Coworkee.Client.Blazor.Data;
using Coworkee.Contracts.Data;

namespace Coworkee.Client.Blazor.Tests;

internal sealed class FakeODataClient : IODataClient
{
    private readonly Dictionary<string, object> _sets = new(StringComparer.Ordinal);
    private IReadOnlyList<FacetGroupDto> _facets = [];

    public FakeODataClient With<T>(string entitySet, params T[] items)
    {
        _sets[entitySet] = items;
        return this;
    }

    public FakeODataClient WithFacets(params FacetGroupDto[] facets)
    {
        _facets = facets;
        return this;
    }

    public event EventHandler<ODataQuery>? Queried;

    public Task<ODataPage<T>> QueryAsync<T>(string entitySet, ODataQuery query, CancellationToken cancellationToken = default)
    {
        Queried?.Invoke(this, query);
        var items = _sets.TryGetValue(entitySet, out var set) ? (T[])set : [];
        // "Prop asc|desc" with $top, enough for the bounds of a column
        if (query.OrderBy?.Split(' ') is [var property, var direction] && typeof(T).GetProperty(property) is { } member)
        {
            var sorted = direction == "desc" ? items.OrderByDescending(i => member.GetValue(i)) : items.OrderBy(i => member.GetValue(i));
            items = [.. sorted.Take(query.Top ?? int.MaxValue)];
        }

        return Task.FromResult(new ODataPage<T>(items, items.Length, query.Facets ? _facets : []));
    }
}
