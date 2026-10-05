namespace Coworkee.OData;

public interface IODataEntityFilter<TEntity>
    where TEntity : class
{
    Task<IQueryable<TEntity>> ApplyAsync(IQueryable<TEntity> query, CancellationToken cancellationToken);
}
