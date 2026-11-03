using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Outbox;
using Coworkee.OData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace Coworkee.Webhooks;

/// <summary>Writes an outbox message per change of an opted-in entity, in the same save, so a change and its webhook commit together.</summary>
// ponytail: one outbox row per change even when no endpoint listens; check the endpoints here (cached) if the opted-in sets change a lot
internal sealed class WebhookChangeInterceptor(IOptions<WebhookOptions> options, ODataEntityRegistry registry, ICurrentUser currentUser, TimeProvider clock)
    : SaveChangesInterceptor
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
        if (context is null || options.Value.Entities.Count == 0)
        {
            return;
        }

        var messages = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted && options.Value.Entities.Contains(e.Metadata.GetRootType().ClrType))
            .Where(e => e.State != EntityState.Modified || e.Properties.Any(p => p.IsModified))
            .Select(e => WebhookOutbox.Message(
                new WebhookEntityChanged(WebhookNames.EntitySet(registry, e.Metadata.GetRootType().ClrType), Key(e), Action(e)), TenantOf(e) ?? currentUser.TenantId, currentUser, clock, e.Entity is IAnonymous))
            .ToList();
        context.Set<OutboxMessage>().AddRange(messages);
    }

    private static string Key(EntityEntry entry) =>
        string.Join(",", entry.Metadata.FindPrimaryKey()!.Properties.Select(p => entry.Property(p.Name).CurrentValue));

    private static Guid? TenantOf(EntityEntry entry) =>
        entry.Metadata.FindProperty(nameof(IMultiTenant.TenantId)) is { } property ? entry.Property(property.Name).CurrentValue as Guid? : null;

    // a soft delete arrives here as an update that sets IsDeleted
    private static string Action(EntityEntry entry) => entry.State switch
    {
        EntityState.Added => WebhookNames.Created,
        EntityState.Deleted => WebhookNames.Deleted,
        _ when entry.Entity is ISoftDelete { IsDeleted: true } && entry.Property(nameof(ISoftDelete.IsDeleted)).IsModified => WebhookNames.Deleted,
        _ => WebhookNames.Updated,
    };
}
