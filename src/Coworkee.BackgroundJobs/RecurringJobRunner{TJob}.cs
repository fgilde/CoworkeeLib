using Coworkee.Core.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.BackgroundJobs;

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
