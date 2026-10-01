using Coworkee.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Infrastructure.Outbox;

public static class OutboxServiceCollectionExtensions
{
    public static IServiceCollection AddCoworkeeOutboxProcessing<TContext>(this IServiceCollection services)
        where TContext : CoworkeeDbContext
    {
        services.AddSingleton<OutboxProcessor<TContext>>();
        services.AddHostedService<OutboxBackgroundService<TContext>>();
        return services;
    }
}
