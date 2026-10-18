using Coworkee.BackgroundJobs;
using Coworkee.Core.Security;

namespace Coworkee.Retention;

/// <summary>A run started on the admin page; the page claimed the target already.</summary>
internal sealed class RetentionRunJob(RetentionRunner runner, ICurrentUser currentUser) : IBackgroundJob<RetentionRunArgs>
{
    public Task ExecuteAsync(RetentionRunArgs args, CancellationToken cancellationToken) => runner.RunAsync(args.Key, args.Days, currentUser.UserId, cancellationToken);
}
