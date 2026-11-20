using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Coworkee.Application.Events;
using Coworkee.Contracts.Realtime;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Coworkee.Infrastructure.Changes;

/// <summary>
/// Scans the change tracker once per save, at the place the audit trail always had (after the soft delete, audit field and tenant interceptors, before
/// snapshots and the outbox), and turns every added, modified and deleted entry (and owners whose owned values changed) into an <see cref="EntityChange"/>:
/// one action mapping (soft delete = Deleted, undoing it = Restored), the audit rules for values. <see cref="EntityChangeSinks"/>, the last interceptor,
/// hands them to the sinks.
/// </summary>
internal sealed class EntityChangeCollector(ICurrentUser currentUser, TimeProvider clock) : SaveChangesInterceptor
{
    private const string Masked = "\"***\"";

    private static readonly HashSet<string> BookkeepingProperties = new(StringComparer.Ordinal)
    {
        nameof(IAuditable.CreatedAt), nameof(IAuditable.CreatedBy), nameof(IAuditable.ModifiedAt), nameof(IAuditable.ModifiedBy),
        nameof(ISoftDelete.IsDeleted), nameof(ISoftDelete.DeletedAt), nameof(ISoftDelete.DeletedBy), nameof(IHasConcurrencyToken.Version),
    };

    /// <summary>The changes of the save in progress, for <see cref="EntityChangeSinks"/>.</summary>
    internal List<EntityChangeEntry> Collected { get; private set; } = [];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Collect(DbContext? context)
    {
        Collected = [];
        if (context is null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var correlationId = Activity.Current?.TraceId.ToHexString();
        var reason = ChangeReason.Current;
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            {
                var audited = IsAudited(entry);
                Collected.Add(Create(entry, audited, audited ? OwnedChanges(entry).ToList() : [], now, correlationId, reason));
            }
            else if (entry.State == EntityState.Unchanged && IsAudited(entry) && OwnedChanges(entry).ToList() is { Count: > 0 } owned)
            {
                Collected.Add(Create(entry, audited: true, owned, now, correlationId, reason));
            }
        }
    }

    private EntityChangeEntry Create(EntityEntry entry, bool audited, List<PropertyChange> owned, DateTimeOffset now, string? correlationId, string? reason)
    {
        var anonymous = entry.Entity is IAnonymous;
        return new EntityChangeEntry(
            new EntityChange
            {
                EntityType = entry.Metadata.GetRootType().ClrType.Name,
                EntityId = KeyOf(entry),
                Action = ActionOf(entry),
                Changes = audited ? [.. PropertyChanges(entry), .. owned] : [],
                Actor = anonymous || (currentUser.UserId is null && currentUser.ImpersonatorId is null) ? null : new ActorInfo(currentUser.UserId, null, currentUser.ImpersonatorId),
                At = now,
                Reason = reason,
                CorrelationId = anonymous ? null : correlationId,
                TenantId = (entry.Metadata.FindProperty(nameof(IMultiTenant.TenantId)) is { } tenant ? entry.Property(tenant.Name).CurrentValue as Guid? : null) ?? currentUser.TenantId,
            },
            entry) { Audited = audited };
    }

    /// <summary>The key as the audit trail writes it: its parts joined by commas, binary keys as base64url.</summary>
    internal static string KeyOf(EntityEntry entry) =>
        string.Join(',', entry.Properties.Where(p => p.Metadata.IsPrimaryKey()).Select(p => p.CurrentValue is byte[] bytes ? System.Buffers.Text.Base64Url.EncodeToString(bytes) : p.CurrentValue));

    internal static bool IsAudited(EntityEntry entry) =>
        !entry.Metadata.IsOwned()
        && entry.Entity is not IAnonymous
        && entry.Metadata.ClrType.GetCustomAttribute<NotAuditedAttribute>() is null
        && entry.Metadata.FindAnnotation(AuditPropertyBuilderExtensions.NotAudited)?.Value is not true;

    internal static ChangeAction ActionOf(EntityEntry entry)
    {
        if (entry.State == EntityState.Added)
        {
            return ChangeAction.Created;
        }

        if (entry.State == EntityState.Deleted)
        {
            return ChangeAction.Deleted;
        }

        if (entry.Entity is ISoftDelete && entry.Property(nameof(ISoftDelete.IsDeleted)) is { IsModified: true } deleted
            && !Equals(deleted.OriginalValue, deleted.CurrentValue))
        {
            return (bool)deleted.CurrentValue! ? ChangeAction.Deleted : ChangeAction.Restored;
        }

        return ChangeAction.Updated;
    }

    private static IEnumerable<PropertyChange> PropertyChanges(EntityEntry entry) =>
        entry.Properties
            .Where(p => !p.Metadata.IsShadowProperty() && !p.Metadata.IsPrimaryKey() && !BookkeepingProperties.Contains(p.Metadata.Name))
            .Where(p => p.Metadata.PropertyInfo?.GetCustomAttribute<NotAuditedAttribute>() is null && !p.Metadata.HasFlag(AuditPropertyBuilderExtensions.NotAudited))
            .Where(p => entry.State is EntityState.Added or EntityState.Deleted || (p.IsModified && !Equals(p.OriginalValue, p.CurrentValue)))
            .Select(p => new PropertyChange(
                p.Metadata.Name,
                entry.State == EntityState.Added ? null : Format(p, p.OriginalValue),
                entry.State == EntityState.Deleted ? null : Format(p, p.CurrentValue)));

    private static IEnumerable<PropertyChange> OwnedChanges(EntityEntry owner) =>
        owner.References
            .Where(r => r.Metadata.TargetEntityType.IsOwned() && r.TargetEntry is { State: EntityState.Added or EntityState.Modified or EntityState.Deleted })
            .Select(r => new PropertyChange(
                r.Metadata.Name,
                r.TargetEntry!.State == EntityState.Added ? null : Snapshot(r.TargetEntry, original: true),
                r.TargetEntry.State == EntityState.Deleted ? null : Snapshot(r.TargetEntry, original: false)));

    private static string Snapshot(EntityEntry owned, bool original) =>
        JsonSerializer.Serialize(owned.Properties
            .Where(p => !p.Metadata.IsShadowProperty() && !p.Metadata.IsKey() && !p.Metadata.HasFlag(AuditPropertyBuilderExtensions.NotAudited))
            .ToDictionary(p => p.Metadata.Name, p => IsSensitive(p.Metadata) ? "***" : original ? p.OriginalValue : p.CurrentValue, StringComparer.Ordinal));

    private static string? Format(PropertyEntry property, object? value) =>
        value is null ? null : IsSensitive(property.Metadata) ? Masked : JsonSerializer.Serialize(value);

    internal static bool IsSensitive(IReadOnlyProperty property) =>
        property.PropertyInfo?.GetCustomAttribute<SensitiveAttribute>() is not null || property.HasFlag(AuditPropertyBuilderExtensions.Sensitive);
}
