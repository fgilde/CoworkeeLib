using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Coworkee.OData;

internal sealed class EntityODataControllerNameProvider(ODataEntityRegistry registry, CoworkeeODataOptions options) : IApplicationModelProvider
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
                            filters[i] = new ScopedEnableQueryAttribute(options);
                        }
                    }
                }
            }
        }
    }

    // after OData's provider (Order 100) wrote the route selectors: the service document and $metadata name every set and property, so they need a sign-in
    public void OnProvidersExecuted(ApplicationModelProviderContext context)
    {
        if (options.AnonymousMetadata)
        {
            return;
        }

        foreach (var selector in context.Result.Controllers
                     .Where(c => c.ControllerType == typeof(Microsoft.AspNetCore.OData.Routing.Controllers.MetadataController))
                     .SelectMany(c => c.Actions).SelectMany(a => a.Selectors))
        {
            selector.EndpointMetadata.Add(new Microsoft.AspNetCore.Authorization.AuthorizeAttribute());
        }
    }
}
