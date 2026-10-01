using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Coworkee.Infrastructure.Persistence.Interceptors;

internal sealed class AuditTrailInterceptor(ICurrentUser currentUser, TimeProvider clock) : SaveChangesInterceptor
{
    private const string Masked = "\"***\"";

    private static readonly HashSet<string> BookkeepingProperties = new(StringComparer.Ordinal)
    {
        nameof(IAuditable.CreatedAt), nameof(IAuditable.CreatedBy), nameof(IAuditable.ModifiedAt), nameof(IAuditable.ModifiedBy),
        nameof(ISoftDelete.IsDeleted), nameof(ISoftDelete.DeletedAt), nameof(ISoftDelete.DeletedBy), nameof(IHasConcurrencyToken.Version),
    };

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
        var correlationId = Activity.Current?.TraceId.ToHexString();
        var entries = context.ChangeTracker.Entries()
            .Where(IsAudited)
            .Select(entry => Create(entry, now, correlationId))
            .OfType<AuditEntry>()
            .ToList();
        context.Set<AuditEntry>().AddRange(entries);
    }

    private static bool IsAudited(EntityEntry entry) =>
        entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted or EntityState.Unchanged
        && !entry.Metadata.IsOwned()
        && entry.Metadata.ClrType.GetCustomAttribute<NotAuditedAttribute>() is null
        && entry.Metadata.FindAnnotation(AuditPropertyBuilderExtensions.NotAudited)?.Value is not true;

    private AuditEntry? Create(EntityEntry entry, DateTimeOffset now, string? correlationId)
    {
        var ownedChanges = OwnedChanges(entry).ToList();
        if (entry.State == EntityState.Unchanged && ownedChanges.Count == 0)
        {
            return null;
        }

        var action = ActionOf(entry);
        var changes = entry.Properties
            .Where(p => !p.Metadata.IsShadowProperty() && !p.Metadata.IsPrimaryKey() && !BookkeepingProperties.Contains(p.Metadata.Name))
            .Where(p => p.Metadata.PropertyInfo?.GetCustomAttribute<NotAuditedAttribute>() is null && !p.Metadata.HasFlag(AuditPropertyBuilderExtensions.NotAudited))
            .Where(p => entry.State is EntityState.Added or EntityState.Deleted || (p.IsModified && !Equals(p.OriginalValue, p.CurrentValue)))
            .Select(p => new AuditChange
            {
                Property = p.Metadata.Name,
                OldValue = entry.State == EntityState.Added ? null : Format(p, p.OriginalValue),
                NewValue = entry.State == EntityState.Deleted ? null : Format(p, p.CurrentValue),
            })
            .Concat(ownedChanges)
            .ToList();

        if (action == AuditAction.Updated && changes.Count == 0)
        {
            return null;
        }

        return new AuditEntry
        {
            TenantId = (entry.Entity as IMultiTenant)?.TenantId ?? currentUser.TenantId,
            EntityType = entry.Metadata.ClrType.Name,
            EntityId = string.Join(',', entry.Properties.Where(p => p.Metadata.IsPrimaryKey()).Select(p => p.CurrentValue)),
            Action = action,
            ActorId = currentUser.UserId,
            CorrelationId = correlationId,
            OccurredAt = now,
            Changes = changes,
        };
    }

    private static AuditAction ActionOf(EntityEntry entry)
    {
        if (entry.State == EntityState.Added)
        {
            return AuditAction.Created;
        }

        if (entry.State == EntityState.Deleted)
        {
            return AuditAction.Deleted;
        }

        if (entry.Entity is ISoftDelete && entry.Property(nameof(ISoftDelete.IsDeleted)) is { IsModified: true } deleted
            && !Equals(deleted.OriginalValue, deleted.CurrentValue))
        {
            return (bool)deleted.CurrentValue! ? AuditAction.Deleted : AuditAction.Restored;
        }

        return AuditAction.Updated;
    }

    private static IEnumerable<AuditChange> OwnedChanges(EntityEntry owner) =>
        owner.References
            .Where(r => r.Metadata.TargetEntityType.IsOwned() && r.TargetEntry is { State: EntityState.Added or EntityState.Modified or EntityState.Deleted })
            .Select(r => new AuditChange
            {
                Property = r.Metadata.Name,
                OldValue = r.TargetEntry!.State == EntityState.Added ? null : Snapshot(r.TargetEntry, original: true),
                NewValue = r.TargetEntry.State == EntityState.Deleted ? null : Snapshot(r.TargetEntry, original: false),
            });

    private static string Snapshot(EntityEntry owned, bool original) =>
        JsonSerializer.Serialize(owned.Properties
            .Where(p => !p.Metadata.IsShadowProperty() && !p.Metadata.IsKey() && !p.Metadata.HasFlag(AuditPropertyBuilderExtensions.NotAudited))
            .ToDictionary(p => p.Metadata.Name, p => IsSensitive(p.Metadata) ? "***" : original ? p.OriginalValue : p.CurrentValue, StringComparer.Ordinal));

    private static string? Format(PropertyEntry property, object? value)
    {
        if (value is null)
        {
            return null;
        }

        return IsSensitive(property.Metadata) ? Masked : JsonSerializer.Serialize(value);
    }

    internal static bool IsSensitive(IReadOnlyProperty property) =>
        property.PropertyInfo?.GetCustomAttribute<SensitiveAttribute>() is not null || property.HasFlag(AuditPropertyBuilderExtensions.Sensitive);
}
