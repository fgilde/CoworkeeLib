using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coworkee.Client.Blazor.ClientEntities.Sqlite;

/// <summary>The copies of client entities in SQLite inside the browser, one database file per copy.</summary>
internal sealed class SqliteClientEntityEngine(IClientEntityStorage storage, ILogger<SqliteClientEntityEngine> logger) : IClientEntityEngine
{
    private static bool? _available;

    public IClientEntityStore<T>? Open<T>(string key) =>
        typeof(T).IsClass && typeof(T).GetProperty("Id")?.PropertyType == typeof(Guid) && Available(logger)
            ? (IClientEntityStore<T>)Activator.CreateInstance(typeof(SqliteClientEntityStore<>).MakeGenericType(typeof(T)), key, storage, logger, null)!
            : null;

    // a client built without WasmBuildNative has no native SQLite: its copies stay columns in memory
    private static bool Available(ILogger logger)
    {
        if (_available is { } known)
        {
            return known;
        }

        try
        {
            using var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            _available = true;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "SQLite is not available in this client (built without <WasmBuildNative>true</WasmBuildNative>?); client entities stay in memory");
            _available = false;
        }

        return _available.Value;
    }
}

public static class SqliteClientEntitiesServiceCollectionExtensions
{
    /// <summary>
    /// Keeps the copies of client entities in SQLite inside the browser instead of columns in memory: a reload opens the stored file, the server
    /// sends a finished download as one SQLite file. The package turns on <c>WasmBuildNative</c> in the client (the wasm-tools workload links SQLite in);
    /// without native SQLite the copies stay in memory. Call next to AddCoworkeeClientEntities.
    /// </summary>
    public static IServiceCollection AddCoworkeeClientEntitiesSqlite(this IServiceCollection services) =>
        services.AddScoped<IClientEntityEngine, SqliteClientEntityEngine>();
}
