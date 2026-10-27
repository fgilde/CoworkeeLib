using System.Linq.Expressions;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData;

/// <summary>Shows a child row only when its parent is visible through the parent's row filters; see <see cref="ODataServiceCollectionExtensions.AddODataChildFilter{TChild, TParent}"/>.</summary>
internal sealed class ODataChildFilter<TChild, TParent>(IServiceProvider services, Expression<Func<TChild, TParent?>> parent) : IODataEntityFilter<TChild>
    where TChild : class
    where TParent : class
{
    public async Task<IQueryable<TChild>> ApplyAsync(IQueryable<TChild> query, CancellationToken cancellationToken)
    {
        var parents = await ODataEntityAccess.VisibleAsync<TParent>(services, cancellationToken);
        var key = services.DbContextFor(typeof(TParent)).Model.FindEntityType(typeof(TParent))!.FindPrimaryKey()!.Properties.Single().Name;

        // child => parents.Any(p => p.Id == child.Parent.Id); EF reads child.Parent.Id from the foreign key, no join
        var p = Expression.Parameter(typeof(TParent), "p");
        var sameKey = Expression.Lambda<Func<TParent, bool>>(Expression.Equal(Expression.Property(p, key), Expression.Property(parent.Body, key)), p);
        var any = Expression.Call(typeof(Queryable), nameof(Queryable.Any), [typeof(TParent)], parents.Expression, Expression.Quote(sameKey));
        return query.Where(Expression.Lambda<Func<TChild, bool>>(any, parent.Parameters));
    }
}
