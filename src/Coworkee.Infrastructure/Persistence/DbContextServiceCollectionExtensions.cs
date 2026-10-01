using Coworkee.Application;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.AddScoped<OutboxInterceptor>();

        services.AddDbContext<TContext>((provider, options) =>
        {
            configure(provider, options);
            options.AddInterceptors(
                provider.GetRequiredService<SoftDeleteInterceptor>(),
                provider.GetRequiredService<AuditablePropertiesInterceptor>(),
                provider.GetRequiredService<MultiTenantInterceptor>(),
                provider.GetRequiredService<AuditTrailInterceptor>(),
                provider.GetRequiredService<OutboxInterceptor>());
            options.AddInterceptors(provider.GetServices<IInterceptor>());
        });
        services.AddScoped<CoworkeeDbContext>(provider => provider.GetRequiredService<TContext>());
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<TContext>());
        return services;
    }
}
