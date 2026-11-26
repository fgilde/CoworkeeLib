using System.Linq.Expressions;

namespace Coworkee.OData.DataFilters;

/// <summary>
/// Tells a group data filter which rows of <typeparamref name="TEntity"/> belong to a group: the rows that come through these groups.
/// The library has no implementation; an app registers one per entity set. The group layer only narrows rows that come through the user's (active) groups;
/// rows that come through none of them are left to the app's own scope.
/// </summary>
public interface IGroupRowSource<TEntity>
{
    /// <summary>
    /// The rows that come through exactly <paramref name="groupIds"/> (a predicate the query translates), not through their sub-groups: the layer passes the
    /// sub-groups a user reaches explicitly, each with its own filters and those of its ancestors.
    /// </summary>
    Task<Expression<Func<TEntity, bool>>> RowsOfAsync(IReadOnlyCollection<Guid> groupIds, CancellationToken ct);

    /// <summary>Whether the current user sees every row without coming through a group (for example realtime admins); a group filter then lets everything pass.</summary>
    Task<bool> SeesAllAsync(CancellationToken ct);
}
