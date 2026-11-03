using System.Collections;
using System.Linq.Expressions;

namespace Coworkee.OData;

/// <summary>
/// A query that goes through the navigation scope when it runs, with everything composed on it later: the facet builder adds its own
/// <c>Where</c>/<c>GroupBy</c> over value and label paths, and those navigations show only the rows their target lists for the user.
/// The targets must be checked before (<see cref="ODataNavigationScope.PrepareAsync(Expression, CancellationToken)"/>); an unchecked one shows no rows.
/// </summary>
internal sealed class ScopedQuery<T>(ScopedQueryProvider provider, Expression expression) : IOrderedQueryable<T>, IAsyncEnumerable<T>
{
    public Type ElementType => typeof(T);

    public Expression Expression => expression;

    public IQueryProvider Provider => provider;

    public IEnumerator<T> GetEnumerator() => provider.Run<T>(expression).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        ((IAsyncEnumerable<T>)provider.Run<T>(expression)).GetAsyncEnumerator(cancellationToken);
}
