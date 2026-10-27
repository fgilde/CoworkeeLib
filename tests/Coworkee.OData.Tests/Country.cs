using Nextended.Core.Facets;

namespace Coworkee.OData.Tests;

/// <summary>A set keyed by a text, not by a Guid Id.</summary>
public sealed class Country
{
    public required string IsoCode { get; set; }

    public required string Name { get; set; }

    [ProvideFacet(Label = "Continent")]
    public required string Continent { get; set; }
}

/// <summary>A set keyed by a number.</summary>
public sealed class Rank
{
    public int Number { get; set; }

    public required string Title { get; set; }
}
