namespace Coworkee.BackgroundJobs;

public interface IBackgroundJobs
{
    string Enqueue<TJob, TArgs>(TArgs args, string queue = "default")
        where TJob : IBackgroundJob<TArgs>;

    /// <summary>Runs the job after <paramref name="delay"/>, as the current user like <see cref="Enqueue{TJob, TArgs}"/>; for waiting on work of other jobs without holding a worker.</summary>
    string Schedule<TJob, TArgs>(TArgs args, TimeSpan delay, string queue = "default")
        where TJob : IBackgroundJob<TArgs>;
}
