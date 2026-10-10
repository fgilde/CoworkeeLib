using Coworkee.AspNetCore.ApplicationServices;
using Microsoft.Extensions.Options;

namespace Coworkee.OData.ClientEntities;

/// <summary>Adds the client entities application services name with ClientEntity&lt;TEntity&gt;().</summary>
internal sealed class ServiceClientEntities(IServiceProvider services) : IPostConfigureOptions<ClientEntityOptions>
{
    public void PostConfigure(string? name, ClientEntityOptions options)
    {
        foreach (var service in services.GetApplicationServices())
        {
            if (service.Properties.TryGetValue(ClientEntityServiceExtensions.Property, out var value) && value is ClientEntityServiceExtensions.ServiceClientEntity entity)
            {
                options.ForService(entity.EntityType, entity.Configure);
            }
        }
    }
}
