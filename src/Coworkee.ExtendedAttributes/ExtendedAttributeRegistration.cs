using Coworkee.OData;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.ExtendedAttributes;

/// <summary>Which entities carry extended attributes, under which name and with which permissions.</summary>
/// <param name="ExistsAsync">Whether the current user sees the row: through the entity's OData set (its permission and row filters) when it has one.</param>
public sealed record ExtendedAttributeRegistration(
    string EntityType, string ViewPermission, string EditPermission, Func<IServiceProvider, Guid, CancellationToken, Task<bool>> ExistsAsync)
{
    internal static ExtendedAttributeRegistration For<TEntity>(string entityType, string viewPermission, string editPermission)
        where TEntity : class =>
        new(entityType, viewPermission, editPermission,
            async (services, id, cancellationToken) => await ODataEntityAccess.VisibleIdsAsync<TEntity>(services, cancellationToken) is { } visible
                                                       && await visible.AnyAsync(x => x == id, cancellationToken));
}
