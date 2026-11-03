using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Coworkee.BackgroundJobs;

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
