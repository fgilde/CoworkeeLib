using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Coworkee.AspNetCore.Http;

public static class CoworkeeApiEndpointExtensions
{
    /// <summary>A group with the conventions of <see cref="CoworkeeApiOptions"/>; a prefix like /api/v2/… maps its endpoints to that API version.</summary>
    public static RouteGroupBuilder MapCoworkeeApi(this IEndpointRouteBuilder endpoints, string prefix)
    {
        // hosts without AddCoworkee have no Asp.Versioning services: their groups stay unversioned
        var group = PathApiVersionReader.VersionOf(prefix) is { } version && endpoints.ServiceProvider.GetService<Asp.Versioning.IApiVersionParameterSource>() is not null
            ? endpoints.NewVersionedApi().MapGroup(prefix).HasApiVersion(version)
            : endpoints.MapGroup(prefix);
        foreach (var convention in endpoints.ServiceProvider.GetRequiredService<IOptions<CoworkeeApiOptions>>().Value.Conventions)
        {
            convention(group);
        }

        return group;
    }
}
