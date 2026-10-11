using System.Data.Common;
using Coworkee.Contracts.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Coworkee.Infrastructure.Persistence;

/// <summary>The database engine behind <see cref="CoworkeeDbContext"/>, chosen by <see cref="DatabaseOptions.Provider"/> (Coworkee:Database:Provider).</summary>
public static class CoworkeeDatabase
{
    public const string MigrationsHistoryTable = "__EFMigrationsHistory";

    public static string Provider(IConfiguration configuration) => DatabaseProviders.Normalize(configuration[DatabaseOptions.ProviderKey]);

    public static string Provider(this DatabaseFacade database) =>
        database.IsSqlServer() ? DatabaseProviders.SqlServer : database.IsSqlite() ? DatabaseProviders.Sqlite : DatabaseProviders.Postgres;

    /// <summary>The provider of the configuration with the connection string <paramref name="connectionStringName"/>.</summary>
    public static DbContextOptionsBuilder UseCoworkeeDatabase(this DbContextOptionsBuilder options, IConfiguration configuration, string connectionStringName) =>
        options.UseCoworkeeDatabase(Provider(configuration), configuration.GetConnectionString(connectionStringName));

    public static DbContextOptionsBuilder UseCoworkeeDatabase(this DbContextOptionsBuilder options, string provider, string? connectionString) =>
        DatabaseProviders.Normalize(provider) switch
        {
            DatabaseProviders.SqlServer => options.UseSqlServer(connectionString, o => o.MigrationsHistoryTable(MigrationsHistoryTable, CoworkeeModelContributor.Schema)),
            // SQLite has no schemas; EF leaves them out, the warning per table says nothing new
            DatabaseProviders.Sqlite => options.UseSqlite(connectionString).ConfigureWarnings(w => w.Ignore(SqliteEventId.SchemaConfiguredWarning)),
            _ => options.UseNpgsql(connectionString, o => o.MigrationsHistoryTable(MigrationsHistoryTable, CoworkeeModelContributor.Schema)),
        };

    /// <summary>A plain connection for reads outside a DbContext; <paramref name="timeout"/> bounds connecting and each command.</summary>
    public static DbConnection CreateConnection(string provider, string connectionString, TimeSpan timeout) =>
        DatabaseProviders.Normalize(provider) switch
        {
            DatabaseProviders.SqlServer => new SqlConnection(new SqlConnectionStringBuilder(connectionString) { ConnectTimeout = Seconds(timeout), CommandTimeout = Seconds(timeout), Pooling = false }.ConnectionString),
            DatabaseProviders.Sqlite => new SqliteConnection(new SqliteConnectionStringBuilder(connectionString) { DefaultTimeout = Seconds(timeout) }.ConnectionString),
            _ => new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connectionString) { Timeout = Seconds(timeout), CommandTimeout = Seconds(timeout) }.ConnectionString),
        };

    /// <summary>A table for raw SQL; SQLite has no schemas, EF leaves them out there.</summary>
    public static string Table(string provider, string schema, string table) =>
        DatabaseProviders.Normalize(provider) == DatabaseProviders.Sqlite ? $"\"{table}\"" : $"\"{schema}\".\"{table}\"";

    private static int Seconds(TimeSpan timeout) => (int)Math.Ceiling(timeout.TotalSeconds);
}
