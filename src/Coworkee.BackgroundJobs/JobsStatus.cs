using Coworkee.Application.Status;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Settings;
using Hangfire;
using Microsoft.Extensions.Options;

namespace Coworkee.BackgroundJobs;

internal sealed class JobsStatus(JobStorage storage, IOptions<BackgroundJobOptions> options) : ISystemStatusContributor
{
    private const int LastFailures = 5;

    public Task<SystemStatusSection> GetAsync(CancellationToken cancellationToken)
    {
        var monitoring = storage.GetMonitoringApi();
        var statistics = monitoring.GetStatistics();
        var failures = monitoring.FailedJobs(0, LastFailures)
            .Select(job => new SystemStatusProblem(
                job.Value.Job is { } run ? $"{run.Type.Name}.{run.Method.Name}" : job.Key, job.Value.ExceptionMessage ?? job.Value.Reason ?? string.Empty,
                job.Value.FailedAt is { } at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : null))
            .ToList();
        return Task.FromResult(new SystemStatusSection("jobs", "Background jobs",
        [
            new("Failed", statistics.Failed.ToString(), statistics.Failed == 0 ? SystemStatusLevel.Ok : SystemStatusLevel.Error),
            new("Scheduled", statistics.Scheduled.ToString()),
            new("Enqueued", statistics.Enqueued.ToString()),
            new("Processing", statistics.Processing.ToString()),
            new("Servers", statistics.Servers.ToString(), statistics.Servers == 0 ? SystemStatusLevel.Warning : SystemStatusLevel.Ok),
        ],
        failures, Order: 2, Link: options.Value.DashboardPath, ExternalLink: true));
    }
}
