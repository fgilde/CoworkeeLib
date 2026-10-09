using Coworkee.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Coworkee.Identity.Users;

/// <summary>Every saved change of a security stamp goes to <see cref="SessionSignal"/>, once it is committed.</summary>
internal sealed class SessionChangeInterceptor(SessionSignal signal, TimeProvider clock) : SaveChangesInterceptor, IDbTransactionInterceptor
{
    private readonly List<SessionChange> _pending = [];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Detect(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Detect(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context?.Database.CurrentTransaction is null)
        {
            await FlushAsync(cancellationToken);
        }

        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context?.Database.CurrentTransaction is null)
        {
            FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        return result;
    }

    public Task TransactionCommittedAsync(System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) =>
        FlushAsync(cancellationToken);

    public void TransactionCommitted(System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData) => FlushAsync(CancellationToken.None).GetAwaiter().GetResult();

    public void TransactionRolledBack(System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData) => _pending.Clear();

    public Task TransactionRolledBackAsync(System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        _pending.Clear();
        return Task.CompletedTask;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => _pending.Clear();

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _pending.Clear();
        return Task.CompletedTask;
    }

    private void Detect(DbContext? context)
    {
        foreach (var entry in context?.ChangeTracker.Entries<User>().Where(e => e.State == EntityState.Modified) ?? [])
        {
            if (entry.Property(u => u.SecurityStamp).OriginalValue != entry.Entity.SecurityStamp)
            {
                _pending.RemoveAll(c => c.UserId == entry.Entity.Id);
                _pending.Add(new SessionChange(entry.Entity.Id, Reason(entry)));
            }
        }
    }

    private string Reason(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<User> entry) =>
        entry.Entity.LockoutEnd > clock.GetUtcNow() && entry.Property(u => u.LockoutEnd).OriginalValue != entry.Entity.LockoutEnd ? "locked"
        : entry.Property(u => u.PasswordHash).OriginalValue != entry.Entity.PasswordHash ? "password-changed"
        : "signed-out";

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (_pending.Count == 0)
        {
            return;
        }

        var changes = _pending.ToList();
        _pending.Clear();
        await signal.PublishAsync(changes, cancellationToken);
    }
}
