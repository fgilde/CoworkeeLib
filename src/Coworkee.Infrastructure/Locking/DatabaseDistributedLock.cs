using System.Data.Common;
using System.Diagnostics;
using Coworkee.Application;
using Coworkee.Contracts.Configuration;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Infrastructure.Locking;

/// <summary>
/// Session level lock on a connection of its own (Postgres advisory lock, SQL Server application lock); the connection closing (crash, failover)
/// releases it too. SQLite has no such lock: a file lock in the temp folder stands in, enough for the processes of one machine.
/// </summary>
internal sealed class DatabaseDistributedLock(IServiceScopeFactory scopes) : IDistributedLock
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    public async Task<IAsyncDisposable?> AcquireAsync(string name, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var (provider, connection) = Open();
        if (provider == DatabaseProviders.Sqlite)
        {
            return await PollAsync(() => Task.FromResult<IAsyncDisposable?>(FileLock.TryAcquire(connection.ConnectionString, name)), timeout, cancellationToken);
        }

        var sql = provider == DatabaseProviders.SqlServer ? SqlServerSql : PostgresSql;
        try
        {
            await connection.OpenAsync(cancellationToken);
            var handle = await PollAsync(
                async () => await ExecuteAsync(connection, sql.TryLock, name, cancellationToken) ? new Handle(connection, sql.Unlock, name) : null,
                timeout, cancellationToken);
            if (handle is null)
            {
                await connection.DisposeAsync();
            }

            return handle;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static readonly (string TryLock, string Unlock) PostgresSql =
        ("SELECT pg_try_advisory_lock(hashtextextended(@name, 0))", "SELECT pg_advisory_unlock(hashtextextended(@name, 0))");

    private static readonly (string TryLock, string Unlock) SqlServerSql =
        ("DECLARE @r int; EXEC @r = sp_getapplock @Resource = @name, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0; SELECT CAST(CASE WHEN @r >= 0 THEN 1 ELSE 0 END AS bit)",
         "EXEC sp_releaseapplock @Resource = @name, @LockOwner = 'Session'; SELECT CAST(1 AS bit)");

    private (string Provider, DbConnection Connection) Open()
    {
        using var scope = scopes.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>().Database;
        var source = database.GetDbConnection();
        return (database.Provider(), source is ICloneable cloneable ? (DbConnection)cloneable.Clone() : source);
    }

    private static async Task<IAsyncDisposable?> PollAsync(Func<Task<IAsyncDisposable?>> attempt, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            if (await attempt() is { } handle)
            {
                return handle;
            }

            if (Stopwatch.GetElapsedTime(started) >= timeout)
            {
                return null;
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    private static async Task<bool> ExecuteAsync(DbConnection connection, string sql, string name, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "name";
        parameter.Value = name;
        command.Parameters.Add(parameter);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private sealed class Handle(DbConnection connection, string unlock, string name) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await using (connection)
            {
                await ExecuteAsync(connection, unlock, name, CancellationToken.None);
            }
        }
    }
}
