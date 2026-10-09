using Coworkee.Core.Modularity;
using Coworkee.Infrastructure.Outbox;
using Coworkee.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Coworkee.EventBus;

/// <summary>Integration events through the outbox; without a transport package they reach the handlers of this app.</summary>
public sealed class CoworkeeEventBusModule : CoworkeeModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelContributor, InboxModelContributor>());
        services.TryAddScoped<IEventBus, OutboxEventBus>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IOutboxRelay, IntegrationEventRelay>());
        services.TryAddSingleton<IntegrationEventDelivery>();
        services.TryAddSingleton<IEventTransport, InProcessTransport>();
    }
}
