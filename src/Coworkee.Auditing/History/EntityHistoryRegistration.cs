using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Domain;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Auditing.History;

/// <summary>An entity set whose records show their history and can go back to an earlier state through the app's own update command.</summary>
public abstract class EntityHistoryRegistration(Type entityType, string entitySet, string restorePermission)
{
    public Type EntityType { get; } = entityType;

    public string EntitySet { get; } = entitySet;

    public string RestorePermission { get; } = restorePermission;

    /// <summary>The record as the current user may read it, soft deleted ones included, not tracked.</summary>
    internal abstract Task<object?> FindAsync(IServiceProvider services, Guid id, CancellationToken cancellationToken);

    internal abstract Task<IReadOnlyList<object>> DeletedAsync(IServiceProvider services, int take, CancellationToken cancellationToken);

    /// <summary>Clears the soft delete flag on the tracked record, so the update command finds it again.</summary>
    internal abstract Task<bool> UndeleteAsync(CoworkeeDbContext db, Guid id, CancellationToken cancellationToken);

    internal abstract Task<Result> SendAsync(IDispatcher dispatcher, object entity, CancellationToken cancellationToken);
}

internal sealed class EntityHistoryRegistration<TEntity, TResult>(string entitySet, string restorePermission, Func<TEntity, IRequest<TResult>> toCommand)
    : EntityHistoryRegistration(typeof(TEntity), entitySet, restorePermission)
    where TEntity : class
    where TResult : Result
{
    internal override async Task<object?> FindAsync(IServiceProvider services, Guid id, CancellationToken cancellationToken) =>
        await (await ODataEntityAccess.VisibleAsync<TEntity>(services, cancellationToken, withDeleted: true))
            .SingleOrDefaultAsync(e => EF.Property<Guid>(e, nameof(Entity.Id)) == id, cancellationToken);

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

public static class EntityHistoryServiceCollectionExtensions
{
    /// <summary>
    /// Opts an OData set into the record history. <paramref name="toCommand"/> turns a record into the app's update command, e.g.
    /// p =&gt; new AddEditProductCommand(p.Id, new AddEditProductRequest { Name = p.Name, ... }); a restore fills the record with the
    /// earlier values and sends that command, so its permissions, validation and audit apply.
    /// </summary>
    public static IServiceCollection AddEntityHistory<TEntity, TResult>(
        this IServiceCollection services, string entitySet, string restorePermission, Func<TEntity, IRequest<TResult>> toCommand)
        where TEntity : class
        where TResult : Result =>
        services.AddSingleton<EntityHistoryRegistration>(new EntityHistoryRegistration<TEntity, TResult>(entitySet, restorePermission, toCommand));
}
