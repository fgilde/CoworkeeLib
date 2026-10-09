using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.EventBus;

/// <summary>Runs every handler of a message in its own scope and transaction; handlers that already processed it are skipped.</summary>
public sealed class IntegrationEventDelivery(IServiceScopeFactory scopes, TimeProvider clock)
{
    private static readonly ConcurrentDictionary<Type, Func<IntegrationEventDelivery, IntegrationMessage, object, CancellationToken, Task>> Deliveries = new();

    private static readonly MethodInfo DeliverTypedMethod =
        typeof(IntegrationEventDelivery).GetMethod(nameof(DeliverTypedAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;

    /// <summary>Throws when a handler failed, after the others ran; a redelivery only retries the failed ones.</summary>
    public Task DeliverAsync(IntegrationMessage message, CancellationToken cancellationToken)
    {
        // an event no contract here knows has no handler here either
        if (Type.GetType(message.EventType) is not { } type)
        {
            return Task.CompletedTask;
        }

        var integrationEvent = JsonSerializer.Deserialize(message.Payload, type)!;
        return Deliveries.GetOrAdd(type, Build)(this, message, integrationEvent, cancellationToken);
    }

    private static Func<IntegrationEventDelivery, IntegrationMessage, object, CancellationToken, Task> Build(Type eventType) =>
        DeliverTypedMethod.MakeGenericMethod(eventType).CreateDelegate<Func<IntegrationEventDelivery, IntegrationMessage, object, CancellationToken, Task>>();

    private async Task DeliverTypedAsync<TEvent>(IntegrationMessage message, object integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(message.ActorId, message.TenantId));
        int count;
        await using (var probe = scopes.CreateAsyncScope())
        {
            count = probe.ServiceProvider.GetServices<IIntegrationEventHandler<TEvent>>().Count();
        }

        List<Exception> failures = [];
        for (var index = 0; index < count; index++)
        {
            try
            {
                await HandleOnceAsync(message, (TEvent)integrationEvent, index, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures.Add(exception);
            }
        }

        if (failures.Count > 0)
        {
            throw new AggregateException($"{failures.Count} handler(s) of integration event {message.Id} failed.", failures);
        }
    }

    private async Task HandleOnceAsync<TEvent>(IntegrationMessage message, TEvent integrationEvent, int index, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        await using var scope = scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetServices<IIntegrationEventHandler<TEvent>>().ElementAt(index);
        var name = handler.GetType().FullName!;
        var db = scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await db.Set<InboxMessage>().AnyAsync(m => m.MessageId == message.Id && m.Handler == name, cancellationToken))
        {
            return;
        }

        await handler.HandleAsync(integrationEvent, cancellationToken);
        db.Set<InboxMessage>().Add(new InboxMessage { MessageId = message.Id, Handler = name, ProcessedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
