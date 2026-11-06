using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coworkee.Client.Blazor.ClientEntities.Sqlite;

/// <summary>PoC: the copies of client entities in SQLite inside the browser, one database file per copy.</summary>
internal sealed class SqliteClientEntityEngine(IClientEntityStorage storage, ILogger<SqliteClientEntityEngine> logger) : IClientEntityEngine
{
    public IClientEntityStore<T>? Open<T>(string key) =>
        typeof(T).IsClass && typeof(T).GetProperty("Id")?.PropertyType == typeof(Guid)
            ? (IClientEntityStore<T>)Activator.CreateInstance(typeof(SqliteClientEntityStore<>).MakeGenericType(typeof(T)), key, storage, logger)!
            : null;
}

public static class SqliteClientEntitiesServiceCollectionExtensions
{
    /// <summary>
    /// PoC: keeps the copies of client entities in SQLite inside the browser instead of columns in memory. Call before AddCoworkeeClientEntities;
    /// the app's client needs <c>&lt;WasmBuildNative&gt;true&lt;/WasmBuildNative&gt;</c> (the wasm-tools workload links SQLite in).
    /// </summary>
    public static IServiceCollection AddCoworkeeClientEntitiesSqlite(this IServiceCollection services) =>
        services.AddScoped<IClientEntityEngine, SqliteClientEntityEngine>();
}
