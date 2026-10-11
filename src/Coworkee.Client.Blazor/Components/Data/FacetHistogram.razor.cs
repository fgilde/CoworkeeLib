using Coworkee.Contracts.Data;
using Microsoft.AspNetCore.Components;

namespace Coworkee.Client.Blazor.Components.Data;

/// <summary>The buckets of a range facet as bars, their height the share of the largest bucket; a bar can pick its range.</summary>
public partial class FacetHistogram
{
    [Parameter] public IReadOnlyList<FacetOptionDto> Buckets { get; set; } = [];

    [Parameter] public Func<FacetOptionDto, bool>? IsActive { get; set; }

    [Parameter] public Func<FacetOptionDto, string>? Title { get; set; }

    [Parameter] public EventCallback<FacetOptionDto> OnPick { get; set; }

    [Parameter] public string? Label { get; set; }

    private string Height(FacetOptionDto bucket)
    {
        var max = Math.Max(1, Buckets.Max(b => b.Count));
        // empty buckets keep a sliver, so the axis stays readable
        return FormattableString.Invariant($"height:{(bucket.Count == 0 ? 3 : 8 + (92.0 * bucket.Count / max)):0.#}%");
    }
}
