using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Client.Blazor.ServiceProxies;

/// <summary>Filled at startup by the generated proxies of the client assembly; AddCoworkeeClient registers them.</summary>
public static class ServiceProxyRegistry
{
    private static readonly ConcurrentDictionary<Type, Action<IServiceCollection, ServiceProxyDescriptor>> Proxies = new();

    public static void Add<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProxy>()
        where TService : class
        where TProxy : class, TService =>
        Proxies[typeof(TService)] = (services, descriptor) =>
        {
            var client = services.AddHttpClient<TService, TProxy>(http =>
            {
                http.BaseAddress = descriptor.BaseAddress;
                if (descriptor.Timeout is { } timeout)
                {
                    http.Timeout = timeout;
                }
            });
            foreach (var configure in descriptor.HttpClient)
            {
                configure(client);
            }
        };

    internal static void AddTo(IServiceCollection services, Uri baseAddress, ServiceProxyOptions options)
    {
        foreach (var (service, register) in Proxies)
        {
            var descriptor = options.Describe(service, baseAddress);
            if (descriptor.Enabled)
            {
                register(services, descriptor);
            }
        }
    }
}
