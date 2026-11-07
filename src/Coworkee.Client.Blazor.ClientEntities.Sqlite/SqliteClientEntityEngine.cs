using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Coworkee.Client.Blazor.ClientEntities.Sqlite;

/// <summary>Which copies <see cref="SqliteClientEntitiesServiceCollectionExtensions.AddCoworkeeClientEntitiesSqlite"/> keeps in SQLite.</summary>
public sealed class SqliteClientEntityOptions
{
    /// <summary>
    /// False (the default): only row types the in-memory copy keeps as JSON (a list or another type it cannot split into columns among their properties);
    /// there SQLite wins everywhere. Row types of simple properties stay columns in memory, whose indexes answer facets about ten times faster.
    /// True: every copy, also those: their first download and a reload are then several times faster, their facets slower (100,000 sales orders:
    /// 9 instead of 34 s until local, about 370 instead of 40 ms for the table's query with its facets).
    /// </summary>
    public bool AllRowTypes { get; set; }
}

/// <summary>The copies of client entities in SQLite inside the browser, one database file per copy.</summary>
internal sealed class SqliteClientEntityEngine(IClientEntityStorage storage, IOptions<SqliteClientEntityOptions> options, ILogger<SqliteClientEntityEngine> logger)
    : IClientEntityEngine
{
    private static bool? _available;

    public IClientEntityStore<T>? Open<T>(string key) =>
        typeof(T).IsClass && typeof(T).GetProperty("Id")?.PropertyType == typeof(Guid) && (options.Value.AllRowTypes || RowShape.Of(typeof(T)) is null) && Available(logger)
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
    /// Keeps copies of client entities in SQLite inside the browser instead of columns in memory: a reload opens the stored file, the server sends a
    /// download as one SQLite file. By default only row types the in-memory copy keeps as JSON (<see cref="SqliteClientEntityOptions.AllRowTypes"/>).
    /// The package turns on <c>WasmBuildNative</c> in the client (the wasm-tools workload links SQLite in); without native SQLite the copies stay in memory.
    /// Call next to AddCoworkeeClientEntities.
    /// </summary>
    public static IServiceCollection AddCoworkeeClientEntitiesSqlite(this IServiceCollection services, Action<SqliteClientEntityOptions>? configure = null)
    {
        services.AddOptions<SqliteClientEntityOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        return services.AddScoped<IClientEntityEngine, SqliteClientEntityEngine>();
    }
}
