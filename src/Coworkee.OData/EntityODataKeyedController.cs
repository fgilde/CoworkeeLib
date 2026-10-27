using System.Linq.Expressions;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Results;

namespace Coworkee.OData;

/// <summary>A set whose key is not a Guid <c>Id</c> (<see cref="ODataEntityOptions{TEntity}.Key"/>): by-key reads take <typeparamref name="TKey"/>, e.g. <c>Countries('DE')</c>.</summary>
public class EntityODataController<TEntity, TKey> : EntityODataController<TEntity>
    where TEntity : class
{
    [EnableQuery]
    public Task<SingleResult<TEntity>> Get([FromODataUri] TKey key)
    {
        var row = Expression.Parameter(typeof(TEntity), "e");
        var lambda = Expression.Lambda<Func<TEntity, bool>>(Expression.Equal(Expression.Property(row, IdPropertyName), Expression.Constant(key, typeof(TKey))), row);
        return Task.FromResult(SingleResult.Create(Queryable().Where(lambda)));
    }
}
