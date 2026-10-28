using System.Collections;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace Coworkee.OData;

/// <summary>
/// A query that goes through the navigation scope when it runs, with everything composed on it later: the facet builder adds its own
/// <c>Where</c>/<c>GroupBy</c> over value and label paths, and those navigations show only the rows their target lists for the user.
/// The targets must be checked before (<see cref="ODataNavigationScope.PrepareAsync(Expression, CancellationToken)"/>); an unchecked one fails.
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

internal sealed class ScopedQueryProvider(IQueryProvider inner, ODataNavigationScope scope) : IAsyncQueryProvider
{
    public static IQueryable<T> Wrap<T>(IQueryable<T> query, ODataNavigationScope scope) =>
        query is ScopedQuery<T> ? query : new ScopedQuery<T>(new ScopedQueryProvider(query.Provider, scope), query.Expression);

    public IQueryable CreateQuery(Expression expression) =>
        (IQueryable)Activator.CreateInstance(typeof(ScopedQuery<>).MakeGenericType(ElementOf(expression.Type)), this, expression)!;

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression) => new ScopedQuery<TElement>(this, expression);

    public object? Execute(Expression expression) => inner.Execute(scope.Rewrite(expression));

    public TResult Execute<TResult>(Expression expression) => inner.Execute<TResult>(scope.Rewrite(expression));

    public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default) =>
        ((IAsyncQueryProvider)inner).ExecuteAsync<TResult>(scope.Rewrite(expression), cancellationToken);

    internal IQueryable<T> Run<T>(Expression expression) => inner.CreateQuery<T>(scope.Rewrite(expression));

    private static Type ElementOf(Type type) =>
        type.GetInterfaces().Append(type).First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)).GenericTypeArguments[0];
}
