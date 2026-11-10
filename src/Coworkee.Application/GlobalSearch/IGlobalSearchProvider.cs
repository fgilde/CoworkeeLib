using Coworkee.Contracts.Search;

namespace Coworkee.Application.GlobalSearch;

/// <summary>
/// One source of the global search (Ctrl+K, <c>/search</c>, the MCP tool <c>global_search</c>). Providers are singletons; each search runs in
/// its own scope (<see cref="GlobalSearchContext.Services"/>) next to the other providers, so a slow one never holds back the fast ones.
/// </summary>
public interface IGlobalSearchProvider
{
    /// <summary>Lower case, unique; also the word of a type filter (<c>users:max</c>).</summary>
    string Name { get; }

    string Group { get; }

    /// <summary>A Material icon name (<c>Person</c>) or SVG markup.</summary>
    string? Icon { get; }

    /// <summary>Users without it never reach <see cref="SearchAsync"/>.</summary>
    string? Permission { get; }

    /// <summary>Multiplies the scores, e.g. 0.8 to rank a noisy source below the others.</summary>
    double Weight => 1;

    /// <summary>How a client finds the same hits in its local copy of an OData set (client entities); null: only the server searches.</summary>
    GlobalSearchLocalDto? Local => null;

    /// <summary>Hits as they are found, best first per batch; scores from 0 to 1 (<see cref="SearchMatch"/>). The searcher stops at the limit.</summary>
    IAsyncEnumerable<GlobalSearchHit> SearchAsync(GlobalSearchQuery query, GlobalSearchContext context, CancellationToken cancellationToken);
}
