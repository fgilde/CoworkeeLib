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
            var controller = typeof(EntityODataController<>).MakeGenericType(entity.EntityType).GetTypeInfo();
            if (!feature.Controllers.Contains(controller))
            {
                feature.Controllers.Add(controller);
            }
        }
    }
}
