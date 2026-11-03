using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Coworkee.Infrastructure.Persistence;

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
