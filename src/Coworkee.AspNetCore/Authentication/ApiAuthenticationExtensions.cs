using Coworkee.Contracts.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.AspNetCore.Authentication;

public static class ApiAuthenticationExtensions
{
    /// <summary>Validates bearer tokens of the Coworkee auth server (<see cref="ApiAuthenticationOptions"/>, set by the Aspire app host).</summary>
    public static IServiceCollection AddCoworkeeApiAuthentication(this IServiceCollection services, IConfiguration configuration, string audience)
    {
        var options = configuration.GetSection(ApiAuthenticationOptions.Section).Get<ApiAuthenticationOptions>() ?? new ApiAuthenticationOptions();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                jwt.Authority = options.Authority;
                jwt.Audience = options.Audience ?? audience;
                jwt.MapInboundClaims = false;
                jwt.RequireHttpsMetadata = !options.Authority.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
            });
        return services;
    }
}
