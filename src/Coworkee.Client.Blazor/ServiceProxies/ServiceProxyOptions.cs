namespace Coworkee.Client.Blazor.ServiceProxies;

/// <summary>Defaults for all service proxies first, then the settings of single services.</summary>
public sealed class ServiceProxyOptions
{
    private readonly List<Action<ServiceProxyDescriptor>> _defaults = [];
    private readonly List<(Type Service, Action<ServiceProxyDescriptor> Configure)> _services = [];

    public ServiceProxyOptions ConfigureAll(Action<ServiceProxyDescriptor> configure)
    {
        _defaults.Add(configure);
        return this;
    }

    public ServiceProxyOptions Configure<TService>(Action<ServiceProxyDescriptor> configure)
    {
        _services.Add((typeof(TService), configure));
        return this;
    }

    internal ServiceProxyDescriptor Describe(Type service, Uri baseAddress)
    {
        var descriptor = new ServiceProxyDescriptor(service, baseAddress);
        _defaults.ForEach(configure => configure(descriptor));
        _services.Where(s => s.Service == service).ToList().ForEach(s => s.Configure(descriptor));
        return descriptor;
    }
}
