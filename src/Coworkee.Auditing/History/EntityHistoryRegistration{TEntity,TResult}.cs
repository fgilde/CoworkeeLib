using Coworkee.Application.Groups;
using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Auditing.History;

internal sealed class EntityHistoryRegistration<TEntity, TResult>(string entitySet, string restorePermission, Func<TEntity, IRequest<TResult>> toCommand)
    : EntityHistoryRegistration(typeof(TEntity), entitySet, restorePermission)
    where TEntity : class
    where TResult : Result
{
    internal override async Task<object?> FindAsync(IServiceProvider services, Guid id, CancellationToken cancellationToken)
    {
        using var unnarrowed = GroupScope.Unnarrowed();
        return await (await ODataEntityAccess.VisibleAsync<TEntity>(services, cancellationToken, withDeleted: true))
            .SingleOrDefaultAsync(e => EF.Property<Guid>(e, nameof(Entity.Id)) == id, cancellationToken);
    }

    internal override async Task<IReadOnlyList<object>> DeletedAsync(IServiceProvider services, int take, CancellationToken cancellationToken)
    {
        if (!typeof(TEntity).IsAssignableTo(typeof(ISoftDelete)))
        {
            return [];
        }

        return await (await ODataEntityAccess.VisibleAsync<TEntity>(services, cancellationToken, withDeleted: true))
            .Where(e => EF.Property<bool>(e, nameof(ISoftDelete.IsDeleted)))
            .OrderByDescending(e => EF.Property<DateTimeOffset?>(e, nameof(ISoftDelete.DeletedAt)))
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    internal override async Task<bool> UndeleteAsync(CoworkeeDbContext db, Guid id, CancellationToken cancellationToken)
    {
        if (await db.Set<TEntity>().IgnoreQueryFilters([CoworkeeDbContext.SoftDeleteFilter])
                .SingleOrDefaultAsync(e => EF.Property<Guid>(e, nameof(Entity.Id)) == id, cancellationToken) is not ISoftDelete { IsDeleted: true } deleted)
        {
            return false;
        }

        deleted.IsDeleted = false;
        deleted.DeletedAt = null;
        deleted.DeletedBy = null;
        return true;
    }

    internal override async Task<Result> SendAsync(IDispatcher dispatcher, object entity, CancellationToken cancellationToken) =>
        await dispatcher.SendAsync(toCommand((TEntity)entity), cancellationToken);
}
