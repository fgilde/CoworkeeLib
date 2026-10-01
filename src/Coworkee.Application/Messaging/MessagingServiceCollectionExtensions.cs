using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Application.Messaging;

public static class MessagingServiceCollectionExtensions
{
    private static readonly Type[] HandlerTypes = [typeof(IHandler<,>), typeof(IDomainEventHandler<>)];

    public static IServiceCollection AddDispatcher(this IServiceCollection services) =>
        services.AddScoped<IDispatcher, Dispatcher>();

    public static IServiceCollection AddMessagingFromAssembly(this IServiceCollection services, Assembly assembly)
    {
        foreach (var type in assembly.GetTypes().Where(t => t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false }))
        {
            foreach (var contract in type.GetInterfaces().Where(i => i.IsGenericType && HandlerTypes.Contains(i.GetGenericTypeDefinition())))
            {
                services.AddScoped(contract, type);
            }
        }

        services.AddValidatorsFromAssembly(assembly, ServiceLifetime.Scoped, includeInternalTypes: true);
        return services;
    }
}
