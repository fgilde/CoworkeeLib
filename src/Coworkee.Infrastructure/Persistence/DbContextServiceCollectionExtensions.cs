using Coworkee.Application;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.DataProtection;
using Coworkee.Infrastructure.Persistence.Interceptors;
using Coworkee.Infrastructure.Versioning;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Coworkee.Infrastructure.Persistence;

public static class DbContextServiceCollectionExtensions
{
    public static IServiceCollection AddCoworkeeDbContext<TContext>(this IServiceCollection services, Action<IServiceProvider, DbContextOptionsBuilder> configure)
        where TContext : CoworkeeDbContext
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ICurrentUser, AnonymousCurrentUser>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelContributor, CoworkeeModelContributor>());
        services.AddScoped<SoftDeleteInterceptor>();
        services.AddScoped<AuditablePropertiesInterceptor>();
        services.AddScoped<MultiTenantInterceptor>();
        services.AddScoped<AuditTrailInterceptor>();
        services.AddScoped<SnapshotInterceptor>();
        services.AddScoped<OutboxInterceptor>();

        services.AddDbContext<TContext>((provider, options) =>
        {
            configure(provider, options);
            options.AddInterceptors(
                provider.GetRequiredService<SoftDeleteInterceptor>(),
                provider.GetRequiredService<AuditablePropertiesInterceptor>(),
                provider.GetRequiredService<MultiTenantInterceptor>(),
                provider.GetRequiredService<AuditTrailInterceptor>(),
                provider.GetRequiredService<SnapshotInterceptor>(),
                provider.GetRequiredService<OutboxInterceptor>());
            options.AddInterceptors(provider.GetServices<IInterceptor>());
        });
        services.AddScoped<CoworkeeDbContext>(provider => provider.GetRequiredService<TContext>());
        services.TryAddSingleton<DbXmlRepository>();
        services.AddDataProtection().SetApplicationName("Coworkee");
        services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(provider =>
            new ConfigureOptions<KeyManagementOptions>(options => options.XmlRepository = provider.GetRequiredService<DbXmlRepository>()));
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<TContext>());
        return services;
    }
}
