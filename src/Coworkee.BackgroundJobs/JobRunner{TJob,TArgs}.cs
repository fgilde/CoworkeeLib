using Coworkee.Core.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.BackgroundJobs;

public sealed class JobRunner<TJob, TArgs>(IServiceProvider services)
    where TJob : IBackgroundJob<TArgs>
{
    public async Task RunAsync(JobEnvelope<TArgs> envelope, CancellationToken cancellationToken)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(envelope.UserId, envelope.TenantId, envelope.ImpersonatorId));
        await using var scope = services.CreateAsyncScope();
        await ActivatorUtilities.GetServiceOrCreateInstance<TJob>(scope.ServiceProvider).ExecuteAsync(envelope.Args, cancellationToken);
    }
}
