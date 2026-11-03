using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace Coworkee.OData;

internal sealed class ScopedQueryProvider(IQueryProvider inner, ODataNavigationScope scope) : IAsyncQueryProvider
{
    public static IQueryable<T> Wrap<T>(IQueryable<T> query, ODataNavigationScope scope) =>
        query is ScopedQuery<T> ? query : new ScopedQuery<T>(new ScopedQueryProvider(query.Provider, scope), query.Expression);

    public IQueryable CreateQuery(Expression expression) =>
        (IQueryable)Activator.CreateInstance(typeof(ScopedQuery<>).MakeGenericType(ElementOf(expression.Type)), this, expression)!;

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression) => new ScopedQuery<TElement>(this, expression);

    public object? Execute(Expression expression) => inner.Execute(Rewrite(expression));

    public TResult Execute<TResult>(Expression expression) => inner.Execute<TResult>(Rewrite(expression));

    public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default) =>
        ((IAsyncQueryProvider)inner).ExecuteAsync<TResult>(Rewrite(expression), cancellationToken);

    internal IQueryable<T> Run<T>(Expression expression) => inner.CreateQuery<T>(Rewrite(expression));

    // a target nobody checked shows no rows instead of failing the request; FacetResponse leaves out groups whose paths were not checked
    private Expression Rewrite(Expression expression) => scope.Rewrite(expression, hideUnchecked: true);

    private static Type ElementOf(Type type) =>
        type.GetInterfaces().Append(type).First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)).GenericTypeArguments[0];
}
