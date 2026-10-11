using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;
using Xunit;

namespace Coworkee.Testing;

public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private Respawner? _respawner;

    public string ConnectionString => _container.GetConnectionString();

    protected virtual string[] SchemasToExclude => [];

    protected virtual IReadOnlyList<(string Schema, string Table)> TablesToKeep => [];

    public virtual async ValueTask InitializeAsync() => await _container.StartAsync();

    public virtual async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        _respawner ??= await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            TablesToIgnore = [new Respawn.Graph.Table("__EFMigrationsHistory"), .. TablesToKeep.Select(t => new Respawn.Graph.Table(t.Schema, t.Table))],
            SchemasToExclude = SchemasToExclude,
        });
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
}
