using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Coworkee.Client.Blazor.Customization;

public static class ComponentReplacementExtensions
{
    public static IServiceCollection ReplaceComponent<TOriginal, TReplacement>(this IServiceCollection services)
        where TOriginal : IComponent
        where TReplacement : IComponent
    {
        services.AddComponentReplacement();
        services.Configure<ComponentReplacementOptions>(options => options.Replace<TOriginal, TReplacement>());
        return services;
    }

    internal static IServiceCollection AddComponentReplacement(this IServiceCollection services)
    {
        services.TryAddSingleton<IComponentActivator, ReplacingComponentActivator>();
        return services;
    }
}
