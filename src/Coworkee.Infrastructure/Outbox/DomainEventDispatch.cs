using System.Collections.Concurrent;
using System.Reflection;
using Coworkee.Application.Messaging;
using Coworkee.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Infrastructure.Outbox;

internal static class DomainEventDispatch
{
    private static readonly ConcurrentDictionary<Type, Func<IServiceProvider, IDomainEvent, CancellationToken, Task>> Dispatchers = new();

    private static readonly MethodInfo DispatchTypedMethod =
        typeof(DomainEventDispatch).GetMethod(nameof(DispatchTypedAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static Task DispatchAsync(IDomainEvent domainEvent, IServiceProvider services, CancellationToken cancellationToken) =>
        Dispatchers.GetOrAdd(domainEvent.GetType(), Build)(services, domainEvent, cancellationToken);

    private static Func<IServiceProvider, IDomainEvent, CancellationToken, Task> Build(Type eventType) =>
        DispatchTypedMethod.MakeGenericMethod(eventType).CreateDelegate<Func<IServiceProvider, IDomainEvent, CancellationToken, Task>>();

    private static async Task DispatchTypedAsync<TEvent>(IServiceProvider services, IDomainEvent domainEvent, CancellationToken cancellationToken)
        where TEvent : IDomainEvent
    {
        foreach (var handler in services.GetServices<IDomainEventHandler<TEvent>>())
        {
            await handler.HandleAsync((TEvent)domainEvent, cancellationToken);
        }
    }
}
