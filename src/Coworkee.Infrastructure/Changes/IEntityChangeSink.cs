using Coworkee.Application.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Coworkee.Infrastructure.Changes;

/// <summary>
/// Gets the changes of every save from the one scan of the change collector. <see cref="Saving"/> runs inside the save, after every other interceptor: rows
/// it adds (audit entries, outbox messages) are saved with the change. <see cref="CommittedAsync"/> runs after the commit (after the save without a
/// transaction), <see cref="Discarded"/> when the save fails or the transaction rolls back. Register it as a scoped <c>IEntityChangeSink</c>.
/// </summary>
public interface IEntityChangeSink
{
    /// <summary>Sinks run in ascending order.</summary>
    int Order => 0;

    void Saving(DbContext context, IReadOnlyList<EntityChangeEntry> changes);

    Task CommittedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    void Discarded()
    {
    }
}

/// <summary>A collected change and the change tracker entry it came from (valid only inside <see cref="IEntityChangeSink.Saving"/>).</summary>
public sealed record EntityChangeEntry(EntityChange Change, EntityEntry Entry)
{
    /// <summary>The audit trail records this entry (and <see cref="EntityChange.Changes"/> hold its values).</summary>
    public bool Audited { get; init; }
}
