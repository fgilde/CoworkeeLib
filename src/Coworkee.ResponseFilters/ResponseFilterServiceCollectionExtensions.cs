using System.Reflection;
using Coworkee.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nextended.ResponseFilters;
using Nextended.ResponseFilters.AspNetCore;

namespace Coworkee.ResponseFilters;

public static class ResponseFilterServiceCollectionExtensions
{
    public static IServiceCollection AddCoworkeeResponseFilters(this IServiceCollection services, Assembly[] assemblies, Action<ResponseFilterOptions>? configure = null)
    {
        services.AddNextendedResponseFilters(assemblies, ServiceLifetime.Scoped, options =>
        {
            configure?.Invoke(options);
            var custom = options.ShouldHandle;
            options.ShouldHandle = (request, type) => IsDeferredQuery(type) ? Task.FromResult(false) : custom?.Invoke(request, type) ?? Task.FromResult(true);
        });
        services.AddScoped<ResponseFilterEndpointFilter>();
        services.Configure<CoworkeeApiOptions>(options => options.AddConvention(group => group.AddEndpointFilter<ResponseFilterEndpointFilter>()));
        return services;
    }

    // OData and other deferred queries are shaped by their own serializer; hide their properties in the model instead (AddODataEntity hidden).
    private static bool IsDeferredQuery(Type type) => typeof(IQueryable).IsAssignableFrom(type);
}
