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
        await _respawner.ResetAsync(connection);
    }
}
