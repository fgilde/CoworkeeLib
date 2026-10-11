using System.Data.Common;
using Coworkee.Contracts.Configuration;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Npgsql;
using Respawn;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Coworkee.Testing;

/// <summary>
/// A fresh database for a test assembly: a Postgres or SQL Server container, or a SQLite file. <see cref="Provider"/> comes from
/// <see cref="ProviderVariable"/> (default Postgres), so a whole test run can switch the engine; an app with one engine overrides it.
/// </summary>
public class DatabaseFixture : IAsyncLifetime
{
    public const string ProviderVariable = "COWORKEE_TEST_DATABASE";

    private IDatabaseContainer? _container;
    private Respawner? _respawner;

    public virtual string Provider { get; } = DatabaseProviders.Normalize(Environment.GetEnvironmentVariable(ProviderVariable));

    public string ConnectionString { get; private set; } = "";

    /// <summary>Connection string and provider under the name the app reads, for a configuration or <c>UseSetting</c>.</summary>
    public IEnumerable<KeyValuePair<string, string?>> Settings(string connectionStringName) =>
        [new($"ConnectionStrings:{connectionStringName}", ConnectionString), new(DatabaseOptions.ProviderKey, Provider)];

    protected virtual string[] SchemasToExclude => [];

    protected virtual IReadOnlyList<(string Schema, string Table)> TablesToKeep => [];

    public virtual async ValueTask InitializeAsync()
    {
        switch (Provider)
        {
            case DatabaseProviders.Sqlite:
                ConnectionString = $"Data Source={Path.Combine(Path.GetTempPath(), $"coworkee-test-{Guid.NewGuid():N}.db")}";
                break;
            case DatabaseProviders.SqlServer:
                _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
                await _container.StartAsync();
                ConnectionString = new SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = "coworkee" }.ConnectionString;
                break;
            default:
                _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
                await _container.StartAsync();
                ConnectionString = _container.GetConnectionString();
                break;
        }
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
        else
        {
            SqliteConnection.ClearAllPools();
            File.Delete(new SqliteConnectionStringBuilder(ConnectionString).DataSource);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Empties every table but the migration history and <see cref="TablesToKeep"/>.</summary>
    public Task ResetAsync() => Provider switch
    {
        DatabaseProviders.Sqlite => ResetSqliteAsync(),
        DatabaseProviders.SqlServer => ResetSqlServerAsync(),
        _ => ResetPostgresAsync(),
    };

    private async Task ResetPostgresAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        _respawner ??= await CreateRespawnerAsync(connection, DbAdapter.Postgres);
        await using (var command = new NpgsqlCommand("SET lock_timeout = '1s'", connection))
        {
            await command.ExecuteNonQueryAsync();
        }

        // Background work (outbox, jobs) can hold row locks while it waits on a second connection; queueing the exclusive
        // TRUNCATE behind it would block that connection too. Give up quickly and retry instead.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await _respawner.ResetAsync(connection);
                return;
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.LockNotAvailable && attempt < 20)
            {
                await Task.Delay(200);
            }
        }
    }

    private async Task ResetSqlServerAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        _respawner ??= await CreateRespawnerAsync(connection, DbAdapter.SqlServer);
        await _respawner.ResetAsync(connection);
    }

    private async Task ResetSqliteAsync()
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var keep = TablesToKeep.Select(t => t.Table).Append("__EFMigrationsHistory").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tables = new List<string>();
        await using (var list = new SqliteCommand("SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'", connection))
        await using (var reader = await list.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        var deletes = string.Concat(tables.Where(t => !keep.Contains(t)).Select(t => $"DELETE FROM \"{t}\";"));
        await using var command = new SqliteCommand($"PRAGMA foreign_keys = OFF; {deletes} PRAGMA foreign_keys = ON;", connection);
        await command.ExecuteNonQueryAsync();
    }

    private Task<Respawner> CreateRespawnerAsync(DbConnection connection, IDbAdapter adapter) =>
        Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = adapter,
            TablesToIgnore = [new Respawn.Graph.Table("__EFMigrationsHistory"), .. TablesToKeep.Select(t => new Respawn.Graph.Table(t.Schema, t.Table))],
            SchemasToExclude = SchemasToExclude,
        });
}
