using Coworkee.BackgroundJobs;
using Coworkee.Core.Security;

namespace Coworkee.Retention.Tests;

public sealed class RecordedJobs
{
    private readonly List<JobEnvelope<RetentionRunArgs>> _jobs = [];

    public int Count
    {
        get
        {
            lock (_jobs)
            {
                return _jobs.Count;
            }
        }
    }

    public void Add(JobEnvelope<RetentionRunArgs> job)
    {
        lock (_jobs)
        {
            _jobs.Add(job);
        }
    }

    public List<JobEnvelope<RetentionRunArgs>> Take()
    {
        lock (_jobs)
        {
            var taken = _jobs.ToList();
            _jobs.Clear();
            return taken;
        }
    }

    public void Clear() => Take();
}

internal sealed class RecordingJobs(RecordedJobs jobs, ICurrentUser currentUser) : IBackgroundJobs
{
    public string Enqueue<TJob, TArgs>(TArgs args, string queue = "default")
        where TJob : IBackgroundJob<TArgs>
    {
        if (args is RetentionRunArgs run)
        {
            jobs.Add(new JobEnvelope<RetentionRunArgs>(run, currentUser.UserId, currentUser.TenantId));
        }

        return Guid.NewGuid().ToString();
    }

    public string Schedule<TJob, TArgs>(TArgs args, TimeSpan delay, string queue = "default")
        where TJob : IBackgroundJob<TArgs> => Enqueue<TJob, TArgs>(args, queue);
}
