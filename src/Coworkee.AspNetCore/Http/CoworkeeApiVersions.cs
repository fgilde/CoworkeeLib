using Asp.Versioning;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.AspNetCore.Http;

public static class CoworkeeApiVersions
{
    /// <summary>
    /// Declares an API version: its OpenAPI document /openapi/v{version}.json and Swagger UI entry. Endpoints map to it with
    /// MapCoworkeeApi("/api/v{version}/…"); version 1 is declared by AddCoworkee and also holds every endpoint without a version.
    /// </summary>
    public static IServiceCollection AddCoworkeeApiVersion(this IServiceCollection services, int version)
    {
        if (services.Any(d => d.ImplementationInstance is CoworkeeApiVersion declared && declared.Version == version))
        {
            return services;
        }

        services.AddSingleton(new CoworkeeApiVersion(version));
        return services.AddOpenApi($"v{version}", options =>
        {
            ApiDocs.AddBearerScheme(options);
            var byGroup = options.ShouldInclude;
            options.ShouldInclude = description => byGroup(description) && Belongs(description, version);
        });
    }

    private static bool Belongs(ApiDescription description, int version) =>
        description.ActionDescriptor.EndpointMetadata.OfType<ApiVersionMetadata>().LastOrDefault() is { } metadata
            ? metadata.IsMappedTo(new ApiVersion(version))
            : version == 1;
}

internal sealed record CoworkeeApiVersion(int Version);
