using Coworkee.Contracts.Search;
using Microsoft.Extensions.DependencyInjection;

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

    /// <summary>Hits as they are found, best first per batch; scores from 0 to 1 (<see cref="SearchMatch"/>). The searcher stops at the limit.</summary>
    IAsyncEnumerable<GlobalSearchHit> SearchAsync(GlobalSearchQuery query, GlobalSearchContext context, CancellationToken cancellationToken);
}

/// <param name="Services">A scope of its own for this provider and search, signed in as the current user.</param>
/// <param name="Limit">Hits wanted from this provider.</param>
public sealed record GlobalSearchContext(IServiceProvider Services, int Limit);

public static class GlobalSearchServiceCollectionExtensions
{
    public static IServiceCollection AddGlobalSearchProvider<TProvider>(this IServiceCollection services)
        where TProvider : class, IGlobalSearchProvider =>
        services.AddSingleton<IGlobalSearchProvider, TProvider>();

    public static IServiceCollection AddGlobalSearchProvider(this IServiceCollection services, IGlobalSearchProvider provider) =>
        services.AddSingleton(provider);
}
