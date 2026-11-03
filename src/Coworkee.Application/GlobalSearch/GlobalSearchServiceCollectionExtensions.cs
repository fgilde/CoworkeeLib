using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Application.GlobalSearch;

public static class GlobalSearchServiceCollectionExtensions
{
    public static IServiceCollection AddGlobalSearchProvider<TProvider>(this IServiceCollection services)
        where TProvider : class, IGlobalSearchProvider =>
        services.AddSingleton<IGlobalSearchProvider, TProvider>();

    public static IServiceCollection AddGlobalSearchProvider(this IServiceCollection services, IGlobalSearchProvider provider) =>
        services.AddSingleton(provider);
}
