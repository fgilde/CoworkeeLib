using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Coworkee.OData;

internal sealed class EntityODataControllerNameProvider(ODataEntityRegistry registry) : IApplicationModelProvider
{
    public int Order => -500;

    public void OnProvidersExecuting(ApplicationModelProviderContext context)
    {
        foreach (var controller in context.Result.Controllers)
        {
            if (controller.ControllerType.IsGenericType
                && controller.ControllerType.GetGenericTypeDefinition() is var definition
                && (definition == typeof(EntityODataController<>) || definition == typeof(EntityODataController<,>))
                && registry.Find(controller.ControllerType.GenericTypeArguments[0]) is { } entity)
            {
                controller.ControllerName = entity.EntitySet;

                // a set keyed otherwise reads by its own key; Nextended's Get(Guid key) would answer the same route
                if (definition == typeof(EntityODataController<,>))
                {
                    foreach (var guidKey in controller.Actions.Where(a => a.ActionMethod.DeclaringType != controller.ControllerType && a.Parameters.Any(p => p.Name == "key")).ToList())
                    {
                        controller.Actions.Remove(guidKey);
                    }
                }

                foreach (var filters in controller.Actions.Select(a => a.Filters))
                {
                    // the inherited [EnableQuery] of Nextended's controller, with navigations narrowed
                    for (var i = 0; i < filters.Count; i++)
                    {
                        if (filters[i].GetType() == typeof(Microsoft.AspNetCore.OData.Query.EnableQueryAttribute))
                        {
                            filters[i] = new ScopedEnableQueryAttribute();
                        }
                    }
                }
            }
        }
    }

    public void OnProvidersExecuted(ApplicationModelProviderContext context)
    {
    }
}
