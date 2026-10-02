using Coworkee.Core.Security;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Coworkee.BackgroundJobs;

public interface IBackgroundJob<in TArgs>
{
    Task ExecuteAsync(TArgs args, CancellationToken cancellationToken);
}

public interface IBackgroundJobs
{
    string Enqueue<TJob, TArgs>(TArgs args, string queue = "default")
        where TJob : IBackgroundJob<TArgs>;
}

/// <summary>A job that runs on a cron schedule, outside of any user or tenant; register with <see cref="RecurringJobExtensions.AddRecurringJob{TJob}"/>.</summary>
public interface IRecurringJob
{
    Task ExecuteAsync(CancellationToken cancellationToken);
}

public sealed record RecurringJobRegistration(string Id, string Cron, Action<IRecurringJobManager, string, string> Schedule);

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

/// <summary>Writes the recurring schedules to job storage at start and keeps retrying while the database is not reachable yet.</summary>
internal sealed class RecurringJobScheduler(IServiceProvider services, ILogger<RecurringJobScheduler> logger) : IHostedService
{
    private readonly CancellationTokenSource _stopping = new();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!TrySchedule())
        {
            _ = RetryAsync(_stopping.Token);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _stopping.Cancel();
        return Task.CompletedTask;
    }

    private async Task RetryAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ContinueWith(_ => { }, TaskScheduler.Default);
            if (!cancellationToken.IsCancellationRequested && TrySchedule())
            {
                return;
            }
        }
    }

    private bool TrySchedule()
    {
        var registrations = services.GetServices<RecurringJobRegistration>().ToList();
        if (registrations.Count == 0)
        {
            return true;
        }

        try
        {
            var manager = services.GetRequiredService<IRecurringJobManager>();
            foreach (var job in registrations)
            {
                job.Schedule(manager, job.Id, job.Cron);
            }

            return true;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Recurring jobs could not be scheduled yet; retrying.");
            return false;
        }
    }
}

public sealed class RecurringJobRunner<TJob>(IServiceProvider services)
    where TJob : IRecurringJob
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TJob>().ExecuteAsync(cancellationToken);
    }
}

public sealed record JobEnvelope<TArgs>(TArgs Args, Guid? UserId, Guid? TenantId);

internal sealed class HangfireBackgroundJobs(IBackgroundJobClient client, ICurrentUser currentUser) : IBackgroundJobs
{
    public string Enqueue<TJob, TArgs>(TArgs args, string queue = "default")
        where TJob : IBackgroundJob<TArgs>
    {
        var envelope = new JobEnvelope<TArgs>(args, currentUser.UserId, currentUser.TenantId);
        return client.Enqueue<JobRunner<TJob, TArgs>>(queue, runner => runner.RunAsync(envelope, CancellationToken.None));
    }
}

public sealed class JobRunner<TJob, TArgs>(IServiceProvider services)
    where TJob : IBackgroundJob<TArgs>
{
    public async Task RunAsync(JobEnvelope<TArgs> envelope, CancellationToken cancellationToken)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(envelope.UserId, envelope.TenantId));
        await using var scope = services.CreateAsyncScope();
        await ActivatorUtilities.GetServiceOrCreateInstance<TJob>(scope.ServiceProvider).ExecuteAsync(envelope.Args, cancellationToken);
    }
}
