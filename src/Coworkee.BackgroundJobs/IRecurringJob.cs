namespace Coworkee.BackgroundJobs;

/// <summary>A job that runs on a cron schedule, outside of any user or tenant; register with <see cref="RecurringJobExtensions.AddRecurringJob{TJob}"/>.</summary>
public interface IRecurringJob
{
    Task ExecuteAsync(CancellationToken cancellationToken);
}
