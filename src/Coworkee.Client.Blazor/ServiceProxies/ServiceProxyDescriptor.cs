using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Client.Blazor.ServiceProxies;

/// <summary>How the generated proxy of one application service is registered; <see cref="ServiceProxyOptions"/> sets it up.</summary>
public sealed class ServiceProxyDescriptor(Type serviceType, Uri baseAddress)
{
    public Type ServiceType { get; } = serviceType;

    public Uri BaseAddress { get; set; } = baseAddress;

    public TimeSpan? Timeout { get; set; }

    /// <summary>False registers no proxy, for example to register an own implementation of the service.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Applied to the HttpClient of the proxy, for example to add a message handler.</summary>
    public IList<Action<IHttpClientBuilder>> HttpClient { get; } = [];

    /// <summary>Settings other features attach to a service, keyed by the feature.</summary>
    public IDictionary<string, object?> Properties { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);
}
