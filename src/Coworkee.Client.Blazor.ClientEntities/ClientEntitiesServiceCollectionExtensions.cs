using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Realtime;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Coworkee.Client.Blazor.ClientEntities;

public static class ClientEntitiesServiceCollectionExtensions
{
    /// <summary>The OData client the local copies load from; resolve it to bypass them.</summary>
    public const string ServerKey = "coworkee.odata.server";

    /// <summary>
    /// Mirrors the OData sets the server marks as client entities and answers their queries locally once they arrived.
    /// Call after AddCoworkeeClient. Register your own <see cref="IClientEntityStorage"/> before to keep the copies elsewhere.
    /// </summary>
    public static IServiceCollection AddCoworkeeClientEntities(this IServiceCollection services)
    {
        var odata = services.LastOrDefault(d => d.ServiceType == typeof(IODataClient) && !d.IsKeyedService)
            ?? throw new InvalidOperationException("Call AddCoworkeeClient before AddCoworkeeClientEntities.");
        services.Remove(odata);
        services.Add(ServiceDescriptor.DescribeKeyed(typeof(IODataClient), ServerKey, (provider, _) => Create(provider, odata), odata.Lifetime));
        services.AddTransient<IODataClient>(provider =>
            new ClientEntityODataClient(provider.GetRequiredKeyedService<IODataClient>(ServerKey), provider.GetRequiredService<ClientEntities>()));
        services.AddHttpClient<ClientEntityApi>((provider, client) => client.BaseAddress = new Uri(provider.GetRequiredService<NavigationManager>().BaseUri));
        services.TryAddScoped<IClientEntityStorage, IndexedDbClientEntityStorage>();
        services.AddSingleton<ClientEntitySignal>();
        services.AddTransient<ClientEntityHttpHandler>();
        services.ConfigureHttpClientDefaults(client => client.AddHttpMessageHandler<ClientEntityHttpHandler>());
        services.AddScoped(provider => new ClientEntities(
            provider.GetRequiredService<ClientEntityApi>(),
            provider.GetRequiredKeyedService<IODataClient>(ServerKey),
            provider.GetRequiredService<IClientEntityStorage>(),
            provider.GetService<RealtimeClient>(),
            provider.GetRequiredService<ClientEntitySignal>(),
            provider.GetRequiredService<ILogger<ClientEntities>>(),
            provider.GetService<Api.ICoworkeeApi>() is { } api ? new ClientEntityModes(api, provider.GetRequiredService<ILogger<ClientEntityModes>>()) : null));
        services.AddScoped<IClientDataCache>(provider => provider.GetRequiredService<ClientEntities>());
        services.AddSingleton(new DataTableToolbarItem(typeof(ClientEntitySwitch)));
        return services;
    }

    private static object Create(IServiceProvider provider, ServiceDescriptor descriptor) =>
        descriptor.ImplementationInstance
        ?? descriptor.ImplementationFactory?.Invoke(provider)
        ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!);
}
