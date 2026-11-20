using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Coworkee.Infrastructure.Changes;

/// <summary>
/// The last interceptor of every save: hands the changes of <see cref="EntityChangeCollector"/> to the <see cref="IEntityChangeSink"/>s inside the save, after
/// the commit (after the save without a transaction), and tells them when the save fails or the transaction rolls back. The entries are live, so a sink
/// sees what every interceptor before it did to them.
/// </summary>
internal sealed class EntityChangeSinks(EntityChangeCollector collector, IEnumerable<IEntityChangeSink> sinks) : SaveChangesInterceptor, IDbTransactionInterceptor
{
    private readonly IEntityChangeSink[] _sinks = [.. sinks.OrderBy(s => s.Order)];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Dispatch(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Dispatch(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context?.Database.CurrentTransaction is null)
        {
            CommitAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context?.Database.CurrentTransaction is null)
        {
            await CommitAsync(cancellationToken);
        }

        return result;
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => CommitAsync(CancellationToken.None).GetAwaiter().GetResult();

    public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) => CommitAsync(cancellationToken);

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => Discard();

    public Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Discard();
        return Task.CompletedTask;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Discard();

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Discard();
        return Task.CompletedTask;
    }

    private void Dispatch(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var changes = collector.Collected;
        foreach (var sink in _sinks)
        {
            sink.Saving(context, changes);
        }
    }

    private async Task CommitAsync(CancellationToken cancellationToken)
    {
        foreach (var sink in _sinks)
        {
            await sink.CommittedAsync(cancellationToken);
        }
    }

    private void Discard()
    {
        foreach (var sink in _sinks)
        {
            sink.Discarded();
        }
    }
}
