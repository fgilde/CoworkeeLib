namespace Coworkee.OData.ClientEntities;

/// <summary>
/// Reports rows as changed to the client entity copies when no <c>ChangedAt</c> column (<c>ModifiedAt</c>) moved, e.g. a tag put on a record
/// through a link table. The next refresh of a copy reloads these rows. Touches join the caller's unit of work and are saved with it; types
/// without a client entity set are ignored. The library's tags, ratings, comments and reactions report themselves.
/// </summary>
public interface IClientEntityChangeLog
{
    void Touch(Type entityType, IEnumerable<Guid> ids);

    void Touch<TEntity>(IEnumerable<Guid> ids)
        where TEntity : class => Touch(typeof(TEntity), ids);

    void Touch<TEntity>(params Guid[] ids)
        where TEntity : class => Touch(typeof(TEntity), ids);
}
