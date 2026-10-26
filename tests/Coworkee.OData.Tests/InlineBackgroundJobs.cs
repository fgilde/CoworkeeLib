using Coworkee.BackgroundJobs;
using Coworkee.Core.Security;

namespace Coworkee.OData.Tests;

/// <summary>Runs a job right away, as Hangfire would run it: in its own scope, as the user that enqueued it.</summary>
internal sealed class InlineBackgroundJobs(ICurrentUser user, InlineBackgroundJobs.Root root) : IBackgroundJobs
{
    public string Enqueue<TJob, TArgs>(TArgs args, string queue = "default")
        where TJob : IBackgroundJob<TArgs>
    {
        var envelope = new JobEnvelope<TArgs>(args, user.UserId, user.TenantId);
        if (root.Held is { } held)
        {
            held.Add(() => new JobRunner<TJob, TArgs>(root.Services).RunAsync(envelope, CancellationToken.None));
            return Guid.NewGuid().ToString();
        }

        new JobRunner<TJob, TArgs>(root.Services).RunAsync(envelope, CancellationToken.None).GetAwaiter().GetResult();
        return Guid.NewGuid().ToString();
    }

    public string Schedule<TJob, TArgs>(TArgs args, TimeSpan delay, string queue = "default")
        where TJob : IBackgroundJob<TArgs> => Enqueue<TJob, TArgs>(args, queue);

    public sealed record Root(IServiceProvider Services)
    {
        /// <summary>When set, jobs wait here instead of running, until the test runs them (outside the request that enqueued them).</summary>
        public List<Func<Task>>? Held { get; set; }
    }
}
