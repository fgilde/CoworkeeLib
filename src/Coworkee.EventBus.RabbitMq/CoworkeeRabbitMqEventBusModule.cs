using System.Reflection;
using Coworkee.Core.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Coworkee.EventBus.RabbitMq;

/// <summary>Sends integration events through RabbitMQ to every app with a handler for them.</summary>
[DependsOn(typeof(CoworkeeEventBusModule))]
public sealed class CoworkeeRabbitMqEventBusModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        var options = context.Configuration.GetSection(RabbitMqOptions.Section).Get<RabbitMqOptions>() ?? new RabbitMqOptions();
        options.ConnectionString ??= context.Configuration.GetConnectionString(RabbitMqOptions.ConnectionStringName);
        options.Queue ??= Assembly.GetEntryAssembly()?.GetName().Name ?? "coworkee";
        services.AddSingleton(options);

        // the Aspire RabbitMQ client integration registers the connection too
        services.TryAddSingleton<IConnection>(_ => new ConnectionFactory
        {
            Uri = new Uri(options.ConnectionString ?? throw new InvalidOperationException($"Connection string '{RabbitMqOptions.ConnectionStringName}' for the event bus is missing.")),
        }.CreateConnectionAsync().GetAwaiter().GetResult());
        services.Replace(ServiceDescriptor.Singleton<IEventTransport, RabbitMqTransport>());

        // read when the host starts, so handlers of modules configured after this one count too
        services.AddSingleton<IHostedService>(provider => new RabbitMqConsumer(
            provider.GetRequiredService<IConnection>(),
            options,
            provider.GetRequiredService<IntegrationEventDelivery>(),
            HandledEventTypes(services),
            provider.GetRequiredService<ILogger<RabbitMqConsumer>>()));
    }

    private static string[] HandledEventTypes(IServiceCollection services) =>
    [
        .. services.Select(d => d.ServiceType)
            .Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IIntegrationEventHandler<>))
            .Select(t => IntegrationMessage.NameOf(t.GetGenericArguments()[0]))
            .Distinct(),
    ];
}
