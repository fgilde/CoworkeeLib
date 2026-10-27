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
                && controller.ControllerType.GetGenericTypeDefinition() == typeof(EntityODataController<>)
                && registry.Find(controller.ControllerType.GenericTypeArguments[0]) is { } entity)
            {
                controller.ControllerName = entity.EntitySet;
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
