using Coworkee.Infrastructure.Persistence;
using Coworkee.OData;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Files.Attachments;

internal sealed class RecordAttachmentType<TEntity>(string entityType) : RecordAttachmentType(entityType)
    where TEntity : class
{
    internal override Task<IQueryable<Guid>?> VisibleIdsAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        ODataEntityAccess.VisibleIdsAsync<TEntity>(services, cancellationToken);

    internal override IQueryable<Guid>? ExistingIds(CoworkeeDbContext db) => db.Model.FindEntityType(typeof(TEntity)) is null ? null : db.Set<TEntity>().IgnoreQueryFilters().Select(e => EF.Property<Guid>(e, "Id"));
}
