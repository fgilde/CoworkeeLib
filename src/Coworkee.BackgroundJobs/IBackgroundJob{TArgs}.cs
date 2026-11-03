namespace Coworkee.BackgroundJobs;

public interface IBackgroundJob<in TArgs>
{
    Task ExecuteAsync(TArgs args, CancellationToken cancellationToken);
}
