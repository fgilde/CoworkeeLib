namespace Coworkee.AspNetCore.ApplicationServices;

/// <summary>Defaults for all application services first, then the settings of single services.</summary>
public sealed class ApplicationServiceOptions
{
    private readonly List<Action<ApplicationServiceDescriptor>> _defaults = [];
    private readonly List<(Type Service, Action<ApplicationServiceDescriptor> Configure)> _services = [];

    public ApplicationServiceOptions ConfigureAll(Action<ApplicationServiceDescriptor> configure)
    {
        _defaults.Add(configure);
        return this;
    }

    public ApplicationServiceOptions Configure<TService>(Action<ApplicationServiceDescriptor> configure)
    {
        _services.Add((typeof(TService), configure));
        return this;
    }

    internal ApplicationServiceDescriptor Describe(ApplicationServiceRegistration registration)
    {
        var descriptor = new ApplicationServiceDescriptor(registration.ServiceType, registration.ImplementationType, registration.Route, registration.Tag);
        _defaults.ForEach(configure => configure(descriptor));
        _services.Where(s => s.Service == descriptor.ServiceType).ToList().ForEach(s => s.Configure(descriptor));
        return descriptor;
    }
}
