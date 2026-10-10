using Coworkee.AspNetCore.ApplicationServices;

namespace Coworkee.OData;

public static class ClientEntityServiceExtensions
{
    internal const string Property = "Coworkee.ClientEntity";

    /// <summary>
    /// Lets clients mirror the OData set of <typeparamref name="TEntity"/>, the entity the service reads and writes; the same as
    /// <see cref="ClientEntityOptions.For{TEntity}"/>, kept with the service. <paramref name="configure"/> applies last.
    /// </summary>
    public static ApplicationServiceDescriptor ClientEntity<TEntity>(this ApplicationServiceDescriptor service, Action<ClientEntitySettings>? configure = null)
        where TEntity : class
    {
        service.Properties[Property] = new ServiceClientEntity(typeof(TEntity), configure);
        return service;
    }

    internal sealed record ServiceClientEntity(Type EntityType, Action<ClientEntitySettings>? Configure);
}
