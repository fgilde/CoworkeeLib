namespace Coworkee.OData.DataFilters;

/// <summary>
/// The data filters of every registered entity set as a row filter: a plain <c>Where</c>, so navigations into the set apply them too
/// (<see cref="ODataNavigationScope"/>). Runs after the app's own filters (<see cref="ODataEntityAccess.Filters{TEntity}"/>).
/// </summary>
internal sealed class DataFilterEntityFilter<TEntity>(ODataEntityRegistry registry, DataFilterResolver resolver) : IODataEntityFilter<TEntity>
    where TEntity : class
{
    public async Task<IQueryable<TEntity>> ApplyAsync(IQueryable<TEntity> query, CancellationToken cancellationToken) =>
        registry.Find(typeof(TEntity)) is { } set && await resolver.PredicateAsync<TEntity>(set.EntitySet, DataFilterScope.IsByKey, cancellationToken) is { } predicate
            ? query.Where(predicate)
            : query;
}
