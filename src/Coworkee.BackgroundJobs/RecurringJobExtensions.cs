using Hangfire;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.BackgroundJobs;

public static class RecurringJobExtensions
{
    public static IServiceCollection AddRecurringJob<TJob>(this IServiceCollection services, string id, string cron)
        where TJob : class, IRecurringJob
    {
        services.AddScoped<TJob>();
        return services.AddSingleton(new RecurringJobRegistration(id, cron, (manager, jobId, schedule) =>
            manager.AddOrUpdate<RecurringJobRunner<TJob>>(jobId, runner => runner.RunAsync(CancellationToken.None), schedule)));
    }
}
