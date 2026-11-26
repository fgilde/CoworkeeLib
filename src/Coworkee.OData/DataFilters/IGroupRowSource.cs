using System.Linq.Expressions;

namespace Coworkee.OData.DataFilters;

/// <summary>
/// Tells a group data filter which rows of <typeparamref name="TEntity"/> belong to a group: the rows that come through these groups.
/// The library has no implementation; an app registers one per entity set.
/// </summary>
public interface IGroupRowSource<TEntity>
{
    /// <summary>The rows that come through <paramref name="groupIds"/> (a predicate the query translates).</summary>
    Task<Expression<Func<TEntity, bool>>> RowsOfAsync(IReadOnlyCollection<Guid> groupIds, CancellationToken ct);

    /// <summary>Whether the current user sees every row without coming through a group (for example realtime admins); a group filter then lets everything pass.</summary>
    Task<bool> SeesAllAsync(CancellationToken ct);
}
