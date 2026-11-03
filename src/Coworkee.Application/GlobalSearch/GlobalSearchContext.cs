namespace Coworkee.Application.GlobalSearch;

/// <param name="Services">A scope of its own for this provider and search, signed in as the current user.</param>
/// <param name="Limit">Hits wanted from this provider.</param>
public sealed record GlobalSearchContext(IServiceProvider Services, int Limit);
