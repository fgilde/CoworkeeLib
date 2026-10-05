using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Coworkee.AspNetCore.Http;

public static class CoworkeeApiEndpointExtensions
{
    public static RouteGroupBuilder MapCoworkeeApi(this IEndpointRouteBuilder endpoints, string prefix)
    {
        var group = endpoints.MapGroup(prefix);
        foreach (var convention in endpoints.ServiceProvider.GetRequiredService<IOptions<CoworkeeApiOptions>>().Value.Conventions)
        {
            convention(group);
        }

        return group;
    }
}
