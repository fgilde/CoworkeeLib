using System.Text.Json;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Auditing;
using Coworkee.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Coworkee.Infrastructure.Versioning;

internal sealed class SnapshotInterceptor(ICurrentUser currentUser, TimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Capture(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var snapshots = context.ChangeTracker.Entries<IVersioned>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList()
            .Select(entry => Create(entry, now))
            .ToList();
        context.Set<EntitySnapshot>().AddRange(snapshots);
    }

    private EntitySnapshot Create(EntityEntry<IVersioned> entry, DateTimeOffset now)
    {
        var deleted = entry.State == EntityState.Deleted;
        var revision = entry.State switch
        {
            EntityState.Added => Math.Max(entry.Entity.Revision, 1),
            _ => (int)entry.Property(nameof(IVersioned.Revision)).OriginalValue! + 1,
        };
        if (!deleted)
        {
            entry.Entity.Revision = revision;
        }

        return new EntitySnapshot
        {
            TenantId = entry.Metadata.FindProperty("TenantId") is { } tenant ? entry.Property(tenant.Name).CurrentValue as Guid? : null,
            EntityType = entry.Metadata.ClrType.Name,
            EntityId = string.Join(",", entry.Metadata.FindPrimaryKey()!.Properties.Select(p => entry.Property(p.Name).CurrentValue)),
            Revision = revision,
            Payload = Payload(entry),
            IsDeleted = deleted,
            CreatedAt = (entry.Entity as IAnonymous)?.Truncate(now) ?? now,
            CreatedBy = entry.Entity is IAnonymous ? null : currentUser.UserId,
        };
    }

    private static string Payload(EntityEntry entry) =>
        JsonSerializer.Serialize(entry.Properties
            .Where(p => !p.Metadata.IsShadowProperty()
                && !p.Metadata.IsConcurrencyToken
                && !p.Metadata.HasFlag(AuditPropertyBuilderExtensions.NotAudited)
                && !AuditTrailInterceptor.IsSensitive(p.Metadata))
            .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue, StringComparer.Ordinal));
}
