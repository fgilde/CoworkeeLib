using Coworkee.Core.Security;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;

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
