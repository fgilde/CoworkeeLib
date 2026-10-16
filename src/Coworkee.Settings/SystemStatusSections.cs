using System.Reflection;
using System.Runtime.InteropServices;
using Coworkee.Application.Status;
using Coworkee.Contracts.Settings;
using Coworkee.Infrastructure.Outbox;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Settings;

internal sealed class VersionsStatus : ISystemStatusContributor
{
    public Task<SystemStatusSection> GetAsync(CancellationToken cancellationToken) => Task.FromResult(new SystemStatusSection("versions", "Versions",
    [
        new("Library", Version(typeof(Coworkee.Core.Modularity.CoworkeeModule).Assembly)),
        new("App", Assembly.GetEntryAssembly() is { } app ? $"{app.GetName().Name} {Version(app)}" : "-"),
        new(".NET", RuntimeInformation.FrameworkDescription),
        new("Operating system", RuntimeInformation.OSDescription),
    ]));

    private static string Version(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? assembly.GetName().Version?.ToString() ?? "-";
}

internal sealed class DatabaseStatus(CoworkeeDbContext db) : ISystemStatusContributor
{
    private const int OutboxWarning = 100;

    public async Task<SystemStatusSection> GetAsync(CancellationToken cancellationToken)
    {
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        var outbox = db.Set<OutboxMessage>().Where(m => m.ProcessedAt == null);
        var backlog = await outbox.CountAsync(cancellationToken);
        var failing = await outbox.Where(m => m.Error != null).OrderByDescending(m => m.OccurredAt).Take(5)
            .Select(m => new { m.Type, m.Error, m.OccurredAt }).ToListAsync(cancellationToken);
        return new SystemStatusSection("database", "Database",
        [
            new("Provider", db.Database.ProviderName ?? "-"),
            new("Server version", await ServerVersionAsync(cancellationToken)),
            new("Pending migrations", pending.Count.ToString(), pending.Count == 0 ? SystemStatusLevel.Ok : SystemStatusLevel.Warning, Detail: pending.Count == 0 ? null : string.Join(", ", pending)),
            new("Outbox backlog", backlog.ToString(), backlog >= OutboxWarning ? SystemStatusLevel.Warning : SystemStatusLevel.Ok),
            new("Outbox messages with errors", failing.Count.ToString(), failing.Count == 0 ? SystemStatusLevel.Ok : SystemStatusLevel.Error),
        ],
        [.. failing.Select(m => new SystemStatusProblem(m.Type, m.Error!, m.OccurredAt))], Order: 1);
    }

    private async Task<string> ServerVersionAsync(CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;
        if (opened)
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            return connection.ServerVersion;
        }
        finally
        {
            if (opened)
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }
}
