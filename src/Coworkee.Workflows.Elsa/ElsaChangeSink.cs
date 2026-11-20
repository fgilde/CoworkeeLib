using System.Text.Json;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Changes;
using Coworkee.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Workflows.Elsa;

/// <summary>
/// Writes an <see cref="EntityChanged"/> to the outbox for every change of a watched entity type (<c>Watch&lt;T&gt;</c>), in the same save, from the change
/// collector's scan. The payload stays as before the collector, so running workflows keep their stimulus: a soft delete arrives as "Updated" here.
/// </summary>
internal sealed class ElsaChangeSink(ElsaWorkflowOptions options, ICurrentUser currentUser, TimeProvider clock) : IEntityChangeSink
{
    public void Saving(DbContext context, IReadOnlyList<EntityChangeEntry> changes)
    {
        if (options.Watched.Count == 0)
        {
            return;
        }

        var watched = changes.Select(c => c.Entry)
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted && options.Watched.ContainsKey(e.Metadata.ClrType))
            .Select(e => (Anonymous: e.Entity is IAnonymous, Change: new EntityChanged(options.Watched[e.Metadata.ClrType], (Guid)e.Property("Id").CurrentValue!, e.State == EntityState.Added ? "Created" : e.State == EntityState.Deleted ? "Deleted" : "Updated")))
            .ToList();
        context.Set<OutboxMessage>().AddRange(watched.Select(c => new OutboxMessage
        {
            Type = typeof(EntityChanged).AssemblyQualifiedName!,
            Payload = JsonSerializer.Serialize(c.Change),
            OccurredAt = clock.GetUtcNow(),
            TenantId = currentUser.TenantId,
            ActorId = c.Anonymous ? null : currentUser.UserId,
            ImpersonatorId = c.Anonymous ? null : currentUser.ImpersonatorId,
        }));
    }
}
