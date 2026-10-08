using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.ExtendedAttributes;

/// <summary>Which entities carry extended attributes, under which name and with which permissions.</summary>
public sealed record ExtendedAttributeRegistration(
    string EntityType, string ViewPermission, string EditPermission, Func<CoworkeeDbContext, Guid, CancellationToken, Task<bool>> ExistsAsync)
{
    internal static ExtendedAttributeRegistration For<TEntity>(string entityType, string viewPermission, string editPermission)
        where TEntity : class =>
        new(entityType, viewPermission, editPermission,
            (db, id, cancellationToken) => db.Set<TEntity>().AnyAsync(e => EF.Property<Guid>(e, "Id") == id, cancellationToken));
}
