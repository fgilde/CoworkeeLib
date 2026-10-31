using System.Linq.Expressions;
using System.Reflection;
using Coworkee.Application.Privacy;
using Coworkee.Domain;
using Coworkee.Infrastructure.Auditing;
using Coworkee.Infrastructure.Outbox;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Infrastructure.Tokens;
using Coworkee.Infrastructure.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Coworkee.Infrastructure.Privacy;

/// <summary>
/// The last step of every erase, after the modules' contributors and a first save: the user's links (access tokens) go, his id leaves the
/// CreatedBy, ModifiedBy and DeletedBy columns of every table, the outbox and the audit trail (<see cref="PersonalDataSubject.ErasedUserId"/> takes its place).
/// The audit trail keeps what happened, when and to which record: the values recorded for the records erased with him (his user, rows keyed by his id,
/// records he created that no longer exist) become <c>***</c>, in all other entries his id and e-mail address are replaced. Snapshots follow the same rules.
/// </summary>
// ponytail: scans the audit trail and the snapshots of the whole database once per erase; an index on the values if erasures become frequent on huge trails
public sealed class PersonalDataReferences(CoworkeeDbContext db)
{
    public const string Erased = "\"***\"";

    private const int Chunk = 500;

    private static readonly string[] AuthorColumns = [nameof(IAuditable.CreatedBy), nameof(IAuditable.ModifiedBy), nameof(ISoftDelete.DeletedBy)];

    private static readonly MethodInfo TombstoneMethod = typeof(PersonalDataReferences).GetMethod(nameof(TombstoneAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo ExistingMethod = typeof(PersonalDataReferences).GetMethod(nameof(ExistingKeysAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;

    public async Task EraseAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        var mentions = new Mentions(subject);
        var gone = await GoneAsync(subject.UserId, cancellationToken);
        await AuditTrailAsync(mentions, gone, cancellationToken);
        await SnapshotsAsync(mentions, gone, cancellationToken);

        var id = subject.UserId;
        var erased = (Guid?)PersonalDataSubject.ErasedUserId;
        await db.Set<AuditEntry>().Where(e => e.ActorId == id).ExecuteUpdateAsync(s => s.SetProperty(e => e.ActorId, erased), cancellationToken);
        await db.Set<AuditEntry>().Where(e => e.ImpersonatorId == id).ExecuteUpdateAsync(s => s.SetProperty(e => e.ImpersonatorId, erased), cancellationToken);
        await db.Set<OutboxMessage>().Where(m => m.ActorId == id).ExecuteUpdateAsync(s => s.SetProperty(m => m.ActorId, erased), cancellationToken);
        await db.Set<OutboxMessage>().Where(m => m.ImpersonatorId == id).ExecuteUpdateAsync(s => s.SetProperty(m => m.ImpersonatorId, erased), cancellationToken);
        await db.Set<AccessToken>().Where(t => t.UserId == id).ExecuteDeleteAsync(cancellationToken);

        foreach (var type in db.Model.GetEntityTypes().Where(t => !t.IsOwned() && !t.HasSharedClrType && t.FindPrimaryKey() is not null && t.GetTableName() is not null))
        {
            foreach (var column in type.GetDeclaredProperties().Where(p => AuthorColumns.Contains(p.Name) && (p.ClrType == typeof(Guid) || p.ClrType == typeof(Guid?))))
            {
                await (Task)TombstoneMethod.MakeGenericMethod(type.ClrType, column.ClrType).Invoke(this, [column.Name, id, cancellationToken])!;
            }
        }
    }

    private async Task AuditTrailAsync(Mentions mentions, IReadOnlyCollection<(string Type, string Id)> gone, CancellationToken cancellationToken)
    {
        var (d, n, email) = (mentions.Id, mentions.CompactId, mentions.Email ?? mentions.Id);
        var entries = await db.Set<AuditEntry>()
            .Where(e => e.EntityId.Contains(d) || e.Changes.Any(c =>
                c.OldValue!.Contains(d) || c.OldValue.Contains(n) || c.OldValue.ToLower().Contains(email)
                || c.NewValue!.Contains(d) || c.NewValue.Contains(n) || c.NewValue.ToLower().Contains(email)))
            .ToListAsync(cancellationToken);
        foreach (var group in gone.GroupBy(r => r.Type))
        {
            foreach (var ids in group.Select(r => r.Id).Chunk(Chunk))
            {
                entries.AddRange(await db.Set<AuditEntry>().Where(e => e.EntityType == group.Key && ids.Contains(e.EntityId)).ToListAsync(cancellationToken));
            }
        }

        var erasedRecords = gone.ToHashSet();
        foreach (var entry in entries.Distinct())
        {
            var all = erasedRecords.Contains((entry.EntityType, entry.EntityId)) || entry.EntityId.Contains(d, StringComparison.Ordinal);
            var changes = entry.Changes.Select(c => new AuditChange
            {
                Property = c.Property,
                OldValue = all ? Blank(c.OldValue) : mentions.Replace(c.OldValue),
                NewValue = all ? Blank(c.NewValue) : mentions.Replace(c.NewValue),
            }).ToList();
            entry.Changes.Clear();
            entry.Changes.AddRange(changes);
        }
    }

    /// <summary>Snapshots of the erased records go; those of records whose trail names him lose his id and address.</summary>
    private async Task SnapshotsAsync(Mentions mentions, IReadOnlyCollection<(string Type, string Id)> gone, CancellationToken cancellationToken)
    {
        var types = await db.Set<EntitySnapshot>().Select(s => s.EntityType).Distinct().ToListAsync(cancellationToken);
        if (types.Count == 0)
        {
            return;
        }

        var id = mentions.UserId;
        var d = mentions.Id;
        var touched = await db.Set<AuditEntry>().Where(e => types.Contains(e.EntityType) && (e.ActorId == id || e.ImpersonatorId == id || e.EntityId.Contains(d)))
            .Select(e => new { e.EntityType, e.EntityId }).Distinct().ToListAsync(cancellationToken);
        var records = touched.Select(r => (r.EntityType, r.EntityId))
            .Concat(db.ChangeTracker.Entries<AuditEntry>().Select(e => (e.Entity.EntityType, e.Entity.EntityId)).Where(r => types.Contains(r.EntityType)))
            .Concat(gone.Where(r => types.Contains(r.Type)))
            .ToHashSet();
        var erasedRecords = gone.ToHashSet();
        foreach (var group in records.GroupBy(r => r.Item1))
        {
            foreach (var ids in group.Select(r => r.Item2).Chunk(Chunk))
            {
                foreach (var snapshot in await db.Set<EntitySnapshot>().Where(s => s.EntityType == group.Key && ids.Contains(s.EntityId)).ToListAsync(cancellationToken))
                {
                    if (erasedRecords.Contains((snapshot.EntityType, snapshot.EntityId)) || snapshot.EntityId.Contains(d, StringComparison.Ordinal))
                    {
                        db.Remove(snapshot);
                    }
                    else if (mentions.Replace(snapshot.Payload) is { } payload && payload != snapshot.Payload)
                    {
                        db.Entry(snapshot).Property(s => s.Payload).CurrentValue = payload;
                    }
                }
            }
        }
    }

    /// <summary>Records the user created that no longer exist: the erase took them, or they were deleted before; either way the trail is all that is left of them.</summary>
    private async Task<IReadOnlyCollection<(string Type, string Id)>> GoneAsync(Guid userId, CancellationToken cancellationToken)
    {
        var created = await db.Set<AuditEntry>().Where(e => e.ActorId == userId && e.Action == AuditAction.Created)
            .Select(e => new { e.EntityType, e.EntityId }).Distinct().ToListAsync(cancellationToken);
        var gone = new List<(string, string)>();
        foreach (var group in created.GroupBy(r => r.EntityType))
        {
            var ids = group.Select(r => r.EntityId).ToList();
            var types = db.Model.GetEntityTypes().Where(t => t.ClrType.Name == group.Key && !t.IsOwned() && !t.HasSharedClrType).ToList();
            HashSet<string> existing = types switch
            {
                [] => [],
                [var type] when type.FindPrimaryKey()?.Properties is [var key] => await ExistingAsync(type, key, ids, cancellationToken),
                // ponytail: composite keys and ambiguous names count as existing; their values stay unless the key holds the user's id
                _ => ids.ToHashSet(),
            };
            gone.AddRange(ids.Where(i => !existing.Contains(i)).Select(i => (group.Key, i)));
        }

        return gone;
    }

    private async Task<HashSet<string>> ExistingAsync(IEntityType type, IProperty key, List<string> ids, CancellationToken cancellationToken)
    {
        var keys = new Dictionary<object, string>();
        foreach (var id in ids)
        {
            object? value = key.ClrType switch
            {
                var t when t == typeof(Guid) => Guid.TryParse(id, out var guid) ? guid : null,
                var t when t == typeof(int) => int.TryParse(id, out var number) ? number : null,
                var t when t == typeof(long) => long.TryParse(id, out var number) ? number : null,
                var t when t == typeof(string) => id,
                _ => null,
            };
            if (value is null)
            {
                return ids.ToHashSet();
            }

            keys[value] = id;
        }

        var found = new HashSet<string>();
        foreach (var chunk in keys.Keys.Chunk(Chunk))
        {
            var task = (Task<List<object>>)ExistingMethod.MakeGenericMethod(type.ClrType, key.ClrType).Invoke(this, [key.Name, chunk, cancellationToken])!;
            found.UnionWith((await task).Select(k => keys[k]));
        }

        return found;
    }

    private async Task<List<object>> ExistingKeysAsync<TEntity, TKey>(string key, object[] values, CancellationToken cancellationToken)
        where TEntity : class
    {
        var typed = values.Cast<TKey>().ToList();
        var found = await db.Set<TEntity>().IgnoreQueryFilters().Select(e => EF.Property<TKey>(e, key)).Where(k => typed.Contains(k)).ToListAsync(cancellationToken);
        return found.Cast<object>().ToList();
    }

    private Task<int> TombstoneAsync<TEntity, TValue>(string column, Guid userId, CancellationToken cancellationToken)
        where TEntity : class
    {
        var entity = Expression.Parameter(typeof(TEntity), "e");
        var value = Expression.Call(typeof(EF), nameof(EF.Property), [typeof(TValue)], entity, Expression.Constant(column));
        var match = Expression.Lambda<Func<TEntity, bool>>(Expression.Equal(value, Expression.Constant((TValue)(object)userId, typeof(TValue))), entity);
        var erased = (TValue)(object)PersonalDataSubject.ErasedUserId;
        return db.Set<TEntity>().IgnoreQueryFilters().Where(match).ExecuteUpdateAsync(s => s.SetProperty(e => EF.Property<TValue>(e, column), erased), cancellationToken);
    }

    private static string? Blank(string? value) => value is null ? null : Erased;

    /// <summary>The forms the user's id and address take in recorded values.</summary>
    private sealed class Mentions(PersonalDataSubject subject)
    {
        public Guid UserId { get; } = subject.UserId;

        public string Id { get; } = subject.UserId.ToString();

        public string CompactId { get; } = subject.UserId.ToString("N");

        public string? Email { get; } = string.IsNullOrWhiteSpace(subject.Email) ? null : subject.Email.Trim().ToLowerInvariant();

        public string? Replace(string? value)
        {
            if (value is null)
            {
                return null;
            }

            var replaced = value.Replace(Id, PersonalDataSubject.ErasedUserId.ToString(), StringComparison.OrdinalIgnoreCase)
                .Replace(CompactId, PersonalDataSubject.ErasedUserId.ToString("N"), StringComparison.OrdinalIgnoreCase);
            return Email is null ? replaced : replaced.Replace(Email, "***", StringComparison.OrdinalIgnoreCase);
        }
    }
}
