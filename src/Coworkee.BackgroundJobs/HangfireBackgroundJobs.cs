using Coworkee.Core.Security;
using Hangfire;

namespace Coworkee.BackgroundJobs;

internal sealed class HangfireBackgroundJobs(IBackgroundJobClient client, ICurrentUser currentUser) : IBackgroundJobs
{
    public string Enqueue<TJob, TArgs>(TArgs args, string queue = "default")
        where TJob : IBackgroundJob<TArgs>
    {
        var envelope = new JobEnvelope<TArgs>(args, currentUser.UserId, currentUser.TenantId, currentUser.ImpersonatorId);
        return client.Enqueue<JobRunner<TJob, TArgs>>(queue, runner => runner.RunAsync(envelope, CancellationToken.None));
    }

    public string Schedule<TJob, TArgs>(TArgs args, TimeSpan delay, string queue = "default")
        where TJob : IBackgroundJob<TArgs>
    {
        var envelope = new JobEnvelope<TArgs>(args, currentUser.UserId, currentUser.TenantId, currentUser.ImpersonatorId);
        return client.Schedule<JobRunner<TJob, TArgs>>(queue, runner => runner.RunAsync(envelope, CancellationToken.None), delay);
    }
}
