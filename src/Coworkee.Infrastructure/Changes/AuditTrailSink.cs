using Coworkee.Application.Events;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Infrastructure.Changes;

/// <summary>An <see cref="AuditEntry"/> per audited change, in the same save; a change without changed values (an update back to the same value) records nothing.</summary>
internal sealed class AuditTrailSink(ICurrentUser currentUser) : IEntityChangeSink
{
    public int Order => -100;

    public void Saving(DbContext context, IReadOnlyList<EntityChangeEntry> changes)
    {
        var entries = new List<AuditEntry>();
        foreach (var (change, entry) in changes.Where(c => c.Audited))
        {
            if (change.Action == ChangeAction.Updated && change.Changes.Count == 0)
            {
                continue;
            }

            entries.Add(new AuditEntry
            {
                TenantId = (entry.Entity as IMultiTenant)?.TenantId ?? currentUser.TenantId,
                EntityType = entry.Metadata.ClrType.Name,
                EntityId = change.EntityId,
                Action = change.Action switch
                {
                    ChangeAction.Created => AuditAction.Created,
                    ChangeAction.Deleted => AuditAction.Deleted,
                    ChangeAction.Restored => AuditAction.Restored,
                    _ => AuditAction.Updated,
                },
                ActorId = currentUser.UserId,
                ImpersonatorId = currentUser.ImpersonatorId,
                CorrelationId = change.CorrelationId,
                OccurredAt = change.At,
                Reason = change.Reason is { Length: > AuditEntry.ReasonLength } reason ? reason[..AuditEntry.ReasonLength] : change.Reason,
                Changes = [.. change.Changes.Select(c => new AuditChange { Property = c.Property, OldValue = c.OldValue, NewValue = c.NewValue })],
            });
        }

        context.Set<AuditEntry>().AddRange(entries);
    }
}
