using Coworkee.Contracts.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Coworkee.Storage;

public static class VirusScanServiceCollectionExtensions
{
    /// <summary>
    /// <see cref="ClamAvVirusScanner"/> as <see cref="IVirusScanner"/> unless one is registered already, and with <c>ConnectionStrings:antivirus</c>
    /// its health check (Services page). <see cref="CoworkeeStorageModule"/> and the auth server call it; an own scanner registered later wins.
    /// </summary>
    public static IServiceCollection AddCoworkeeVirusScanner(this IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(s => s.ServiceType == typeof(IVirusScanner)))
        {
            return services;
        }

        services.AddSingleton<IVirusScanner, ClamAvVirusScanner>();
        if (configuration.GetConnectionString(ClamAvVirusScanner.ConnectionStringName) is { Length: > 0 })
        {
            services.AddHealthChecks().AddCheck<ClamAvHealthCheck>(ClamAvHealthCheck.Name, tags: [CoworkeeServicesOptions.HealthCheckTag]);
        }

        return services;
    }
}
