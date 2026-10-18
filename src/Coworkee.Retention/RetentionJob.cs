using Coworkee.Application.Retention;
using Coworkee.BackgroundJobs;
using Coworkee.Settings;

namespace Coworkee.Retention;

/// <summary>The nightly run: every target with days set, one after the other; a failing one does not stop the others.</summary>
internal sealed class RetentionJob(IEnumerable<IRetentionTarget> targets, ISettingProvider settings, RetentionRunner runner) : IRecurringJob
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        foreach (var key in targets.Select(t => t.Key).ToList())
        {
            if (RetentionSettingDefinitions.Days(await settings.GetAsync(RetentionSettingDefinitions.Name(key), cancellationToken)) is { } days
                && await runner.TryClaimAsync(key, cancellationToken))
            {
                await runner.RunAsync(key, days, null, cancellationToken);
            }
        }
    }
}
