using Coworkee.Contracts.Configuration;
using Microsoft.Data.SqlClient;

namespace Coworkee.Infrastructure.Tests;

/// <summary>A reset that SQL Server picks as the victim of a deadlock with the app's work runs again instead of failing the test.</summary>
public sealed class SqlServerResetTests(SqlServerResetTests.SqlServer database) : IClassFixture<SqlServerResetTests.SqlServer>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_reset_chosen_as_deadlock_victim_runs_again()
    {
        await database.ExecuteAsync("INSERT INTO parent (id, x) VALUES (1, 0); INSERT INTO child (id, parent_id, x) VALUES (1, 1, 0);");

        // the app's work: holds the parent row, and wants the child row only once the reset holds it
        await using var work = new SqlConnection(database.ConnectionString);
        await work.OpenAsync(Ct);
        var spid = (short)(await new SqlCommand("SELECT @@SPID", work).ExecuteScalarAsync(Ct))!;
        await using var transaction = work.BeginTransaction();
        await new SqlCommand("SET DEADLOCK_PRIORITY NORMAL; UPDATE parent SET x = 1 WHERE id = 1", work, transaction).ExecuteNonQueryAsync(Ct);

        // the reset deletes the child rows first (foreign key order) and waits for the parent row
        var reset = database.ResetAsync();
        while (await database.ScalarAsync($"SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id = {spid}") == 0)
        {
            reset.IsCompleted.ShouldBeFalse();
            await Task.Delay(20, Ct);
        }

        await new SqlCommand("UPDATE child SET x = 1 WHERE id = 1", work, transaction).ExecuteNonQueryAsync(Ct);
        await transaction.CommitAsync(Ct);

        await reset.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        (await database.ScalarAsync("SELECT (SELECT COUNT(*) FROM parent) + (SELECT COUNT(*) FROM child)")).ShouldBe(0);
    }

    public sealed class SqlServer : Coworkee.Testing.DatabaseFixture
    {
        public override string Provider => DatabaseProviders.SqlServer;

        public override async ValueTask InitializeAsync()
        {
            await base.InitializeAsync();
            await using (var master = new SqlConnection(new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" }.ConnectionString))
            {
                await master.OpenAsync();
                await new SqlCommand("CREATE DATABASE coworkee", master).ExecuteNonQueryAsync();
            }

            await ExecuteAsync("CREATE TABLE parent (id int PRIMARY KEY, x int NOT NULL); CREATE TABLE child (id int PRIMARY KEY, parent_id int NOT NULL REFERENCES parent (id), x int NOT NULL);");
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await new SqlCommand(sql, connection).ExecuteNonQueryAsync();
        }

        public async Task<int> ScalarAsync(string sql)
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            return (int)(await new SqlCommand(sql, connection).ExecuteScalarAsync())!;
        }
    }
}
