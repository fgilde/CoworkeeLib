using System.Diagnostics;
using System.Text.Json;
using Coworkee.Core.Security;
using Coworkee.Domain;
using Coworkee.Infrastructure.Outbox;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace Coworkee.Webhooks;

/// <summary>Sends an app event to every active endpoint that chose it; it is stored with the unit of work and leaves after the commit.</summary>
public interface IWebhookPublisher
{
    /// <param name="type">E.g. "order.shipped"; list it with <see cref="WebhookOptions.Event"/> so admins can pick it.</param>
    /// <param name="data">Sent as it is: leave out what not every receiving admin may read.</param>
    Task PublishAsync(string type, object data, CancellationToken cancellationToken = default);
}

public sealed record WebhookAppEvent(string Type, JsonElement Data);

public sealed record WebhookEntityChanged(string EntityType, string Key, string Action);

internal sealed class WebhookPublisher(CoworkeeDbContext db, ICurrentUser currentUser, TimeProvider clock) : IWebhookPublisher
{
    public Task PublishAsync(string type, object data, CancellationToken cancellationToken = default)
    {
        db.Add(WebhookOutbox.Message(new WebhookAppEvent(type, JsonSerializer.SerializeToElement(data, JsonSerializerOptions.Web)), currentUser.TenantId, currentUser, clock));
        return Task.CompletedTask;
    }
}

internal static class WebhookOutbox
{
    public static OutboxMessage Message(object payload, Guid? tenantId, ICurrentUser currentUser, TimeProvider clock) => new()
    {
        Type = payload.GetType().AssemblyQualifiedName!,
        Payload = JsonSerializer.Serialize(payload, payload.GetType()),
        OccurredAt = clock.GetUtcNow(),
        TenantId = tenantId,
        ActorId = currentUser.UserId,
        CorrelationId = Activity.Current?.TraceId.ToHexString(),
    };
}

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
                new WebhookEntityChanged(WebhookNames.EntitySet(registry, e.Metadata.GetRootType().ClrType), Key(e), Action(e)), TenantOf(e) ?? currentUser.TenantId, currentUser, clock))
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

internal static class WebhookNames
{
    public const string Created = "created";
    public const string Updated = "updated";
    public const string Deleted = "deleted";

    public static string EntitySet(ODataEntityRegistry registry, Type entityType) =>
        registry.Find(entityType)?.EntitySet ?? throw new InvalidOperationException($"{entityType.Name} sends webhooks but is no OData set: register it with AddODataEntity.");

    /// <summary>"Products" and "created" become "products.created", "OrderLines" becomes "order_lines.…".</summary>
    public static string EventType(string entitySet, string action) => $"{JsonNamingPolicy.SnakeCaseLower.ConvertName(entitySet)}.{action}";
}
