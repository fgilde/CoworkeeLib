using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Identity.Sessions;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Identity.Users;

/// <summary>
/// Every saved change of a security stamp and every ended <see cref="UserSession"/> goes to <see cref="SessionSignal"/>, once it is committed. Then the
/// listeners revoke an ended session's tokens, and a new stamp ends the rows of the sessions it ended, so every way a session ends shows in the list.
/// </summary>
internal sealed class SessionChangeInterceptor(SessionSignal signal, TimeProvider clock, IServiceScopeFactory scopes, Microsoft.AspNetCore.Http.IHttpContextAccessor http) : SaveChangesInterceptor, IDbTransactionInterceptor
{
    private readonly List<SessionChange> _pending = [];

    // sessions that ended without a signal of their own: their tokens are revoked all the same
    private readonly List<SessionChange> _quiet = [];

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

    public void TransactionRolledBack(System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData) => Clear();

    public Task TransactionRolledBackAsync(System.Data.Common.DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Clear();
        return Task.CompletedTask;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Clear();

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Clear();
        return Task.CompletedTask;
    }

    private void Detect(DbContext? context)
    {
        foreach (var entry in context?.ChangeTracker.Entries<User>().Where(e => e.State == EntityState.Modified) ?? [])
        {
            if (entry.Property(u => u.SecurityStamp).OriginalValue != entry.Entity.SecurityStamp)
            {
                _pending.RemoveAll(c => c.UserId == entry.Entity.Id);
                // KeptSession is "{sid} {previous stamp} {stamp}" (SessionStamp.Keep)
                var kept = entry.Entity.KeptSession is { } session && entry.Property(u => u.KeptSession).IsModified ? session.Split(' ')[0] : null;
                _pending.Add(new SessionChange(entry.Entity.Id, Reason(entry), kept));
            }
        }

        foreach (var entry in context?.ChangeTracker.Entries<UserSession>().Where(e => e.State == EntityState.Modified) ?? [])
        {
            var session = entry.Entity;
            if (session.EndedAt is null || entry.Property(s => s.EndedAt).OriginalValue is not null)
            {
                continue;
            }

            var change = new SessionChange(session.UserId, session.EndReason!, EndedSessionId: session.Sid);
            if (session.Quiet || _pending.Any(c => c.UserId == session.UserId && c.EndedSessionId is null))
            {
                _quiet.Add(change);
            }
            else
            {
                _pending.Add(change);
            }
        }
    }

    private string Reason(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<User> entry) =>
        entry.Entity.LockoutEnd > clock.GetUtcNow() && entry.Property(u => u.LockoutEnd).OriginalValue != entry.Entity.LockoutEnd ? "locked"
        : entry.Property(u => u.PasswordHash).OriginalValue != entry.Entity.PasswordHash ? "password-changed"
        : "signed-out";

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (_pending.Count + _quiet.Count == 0)
        {
            return;
        }

        var changes = _pending.ToList();
        var quiet = _quiet.ToList();
        Clear();
        if (changes.Count > 0)
        {
            await signal.PublishAsync(changes, cancellationToken);
        }

        await EndAsync([.. changes, .. quiet], cancellationToken);
    }

    private void Clear()
    {
        _pending.Clear();
        _quiet.Clear();
    }

    // a scope of its own: the committed unit of work is done, the revocation and the rows are a unit of their own
    private async Task EndAsync(List<SessionChange> changes, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        foreach (var listener in scope.ServiceProvider.GetServices<IUserSessionListener>())
        {
            foreach (var change in changes.Where(c => c.EndedSessionId is not null))
            {
                await listener.SessionEndedAsync(change.UserId, change.EndedSessionId!, cancellationToken);
            }
        }

        var stamps = changes.Where(c => c.EndedSessionId is null).ToList();
        if (stamps.Count == 0)
        {
            return;
        }

        var db = scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>();
        var by = scope.ServiceProvider.GetRequiredService<ICurrentUser>().UserId;
        var signingInAgain = http.HttpContext?.Items[SessionStamp.RequestSessionItem] as string;
        var users = stamps.Select(c => c.UserId).ToList();
        var now = clock.GetUtcNow();
        foreach (var session in await db.Set<UserSession>().IgnoreQueryFilters([CoworkeeDbContext.TenantFilter])
                     .Where(s => users.Contains(s.UserId) && s.EndedAt == null).ToListAsync(cancellationToken))
        {
            var change = stamps.First(c => c.UserId == session.UserId);
            if (change.KeptSessionId != session.Sid && signingInAgain != session.Sid)
            {
                session.End(now, change.Reason, by, quiet: true);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
