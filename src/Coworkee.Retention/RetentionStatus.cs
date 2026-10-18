using System.Globalization;
using Coworkee.Application.Retention;
using Coworkee.Application.Status;
using Coworkee.Contracts.Settings;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Retention;

/// <summary>The last run of every target and its error.</summary>
internal sealed class RetentionStatus(IEnumerable<IRetentionTarget> targets, CoworkeeDbContext db) : ISystemStatusContributor
{
    public async Task<SystemStatusSection> GetAsync(CancellationToken cancellationToken)
    {
        var runs = await db.Set<RetentionRun>().AsNoTracking().ToDictionaryAsync(r => r.Key, StringComparer.Ordinal, cancellationToken);
        var entries = new List<SystemStatusEntry>();
        var problems = new List<SystemStatusProblem>();
        foreach (var target in targets)
        {
            var run = runs.GetValueOrDefault(target.Key);
            entries.Add(run?.LastRunAt is { } at
                ? new SystemStatusEntry(target.Title, at.ToString("u", CultureInfo.InvariantCulture), run.LastError is null ? SystemStatusLevel.Ok : SystemStatusLevel.Error,
                    Detail: run.LastDeleted?.ToString(CultureInfo.InvariantCulture))
                : SystemStatusEntry.Text(target.Title, "Not run yet"));
            if (run?.LastError is { } error)
            {
                problems.Add(new SystemStatusProblem(target.Title, error, run.LastRunAt));
            }
        }

        return new SystemStatusSection("retention", "Retention", entries, problems, Order: 3, Link: "/admin/retention");
    }
}
