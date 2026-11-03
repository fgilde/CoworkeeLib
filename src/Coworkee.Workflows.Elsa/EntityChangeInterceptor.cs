using System.Text.Json;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Coworkee.Workflows.Elsa;

/// <summary>Writes an <see cref="EntityChanged"/> to the outbox for every change of a watched entity type, in the same save.</summary>
internal sealed class EntityChangeInterceptor(ElsaWorkflowOptions options, ICurrentUser currentUser, TimeProvider clock) : SaveChangesInterceptor
{
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
        if (context is null || options.Watched.Count == 0)
        {
            return;
        }

        var changes = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted && options.Watched.ContainsKey(e.Metadata.ClrType))
            .Select(e => (Anonymous: e.Entity is IAnonymous, Change: new EntityChanged(options.Watched[e.Metadata.ClrType], (Guid)e.Property("Id").CurrentValue!, e.State == EntityState.Added ? "Created" : e.State == EntityState.Deleted ? "Deleted" : "Updated")))
            .ToList();
        context.Set<OutboxMessage>().AddRange(changes.Select(c => new OutboxMessage
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
