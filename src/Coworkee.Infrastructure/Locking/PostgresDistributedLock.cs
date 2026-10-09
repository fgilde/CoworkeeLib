using System.Diagnostics;
using Coworkee.Application;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Coworkee.Infrastructure.Locking;

/// <summary>Session level advisory lock on a connection of its own; the connection closing (crash, failover) releases it too.</summary>
internal sealed class PostgresDistributedLock(IServiceScopeFactory scopes) : IDistributedLock
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    public async Task<IAsyncDisposable?> AcquireAsync(string name, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var connection = await OpenAsync(cancellationToken);
        try
        {
            var started = Stopwatch.GetTimestamp();
            while (!await ExecuteAsync<bool>(connection, "SELECT pg_try_advisory_lock(hashtextextended($1, 0))", name, cancellationToken))
            {
                if (Stopwatch.GetElapsedTime(started) >= timeout)
                {
                    await connection.DisposeAsync();
                    return null;
                }

                await Task.Delay(PollInterval, cancellationToken);
            }

            return new Handle(connection, name);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var source = (NpgsqlConnection)scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>().Database.GetDbConnection();
        var connection = (NpgsqlConnection)((ICloneable)source).Clone();
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task<T> ExecuteAsync<T>(NpgsqlConnection connection, string sql, string name, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection) { Parameters = { new() { Value = name } } };
        return (T)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private sealed class Handle(NpgsqlConnection connection, string name) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await using (connection)
            {
                await ExecuteAsync<bool>(connection, "SELECT pg_advisory_unlock(hashtextextended($1, 0))", name, CancellationToken.None);
            }
        }
    }
}
