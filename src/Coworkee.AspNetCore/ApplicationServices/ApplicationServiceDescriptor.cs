using Microsoft.AspNetCore.Routing;

namespace Coworkee.AspNetCore.ApplicationServices;

/// <summary>How one application service is exposed: starts from the conventions and [ApplicationService], then <see cref="ApplicationServiceOptions"/> applies.</summary>
public sealed class ApplicationServiceDescriptor(Type serviceType, Type implementationType, string route, string tag)
{
    public Type ServiceType { get; } = serviceType;

    public Type ImplementationType { get; } = implementationType;

    public string Route { get; set; } = route;

    public string Tag { get; set; } = tag;

    /// <summary>False keeps the service off the API; it stays registered for calls in process.</summary>
    public bool Enabled { get; set; } = true;

    public bool RequireAuthorization { get; set; } = true;

    /// <summary>Applied to the endpoint group of the service, for example rate limits or output caching.</summary>
    public IList<Action<RouteGroupBuilder>> Conventions { get; } = [];

    /// <summary>Settings other features attach to a service, keyed by the feature.</summary>
    public IDictionary<string, object?> Properties { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);
}
