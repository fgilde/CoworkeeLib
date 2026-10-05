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
        services.AddNextendedResponseFilters(assemblies, ServiceLifetime.Scoped, configure);
        services.AddScoped<ResponseFilterEndpointFilter>();
        services.Configure<CoworkeeApiOptions>(options => options.AddConvention(group => group.AddEndpointFilter<ResponseFilterEndpointFilter>()));
        return services;
    }
}
