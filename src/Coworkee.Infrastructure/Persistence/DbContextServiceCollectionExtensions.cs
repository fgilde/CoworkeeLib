using Coworkee.Application;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
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
        services.AddScoped<SoftDeleteInterceptor>();
        services.AddScoped<AuditablePropertiesInterceptor>();
        services.AddScoped<MultiTenantInterceptor>();

        services.AddDbContext<TContext>((provider, options) =>
        {
            configure(provider, options);
            options.AddInterceptors(
                provider.GetRequiredService<SoftDeleteInterceptor>(),
                provider.GetRequiredService<AuditablePropertiesInterceptor>(),
                provider.GetRequiredService<MultiTenantInterceptor>());
        });
        services.AddScoped<CoworkeeDbContext>(provider => provider.GetRequiredService<TContext>());
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<TContext>());
        return services;
    }
}
