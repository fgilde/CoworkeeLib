using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace Coworkee.OData;

internal sealed class EntityODataControllerFeatureProvider(ODataEntityRegistry registry) : IApplicationFeatureProvider<ControllerFeature>
{
    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        foreach (var entity in registry.Entities)
        {
            var controller = (entity.KeyType == typeof(Guid)
                ? typeof(EntityODataController<>).MakeGenericType(entity.EntityType)
                : typeof(EntityODataController<,>).MakeGenericType(entity.EntityType, entity.KeyType)).GetTypeInfo();
            if (!feature.Controllers.Contains(controller))
            {
                feature.Controllers.Add(controller);
            }
        }

        // the service document and $metadata on every route, whether or not the host's part discovery found OData's assembly; the sign-in comes from
        // EntityODataControllerNameProvider
        var metadata = typeof(Microsoft.AspNetCore.OData.Routing.Controllers.MetadataController).GetTypeInfo();
        if (!feature.Controllers.Contains(metadata))
        {
            feature.Controllers.Add(metadata);
        }
    }
}
