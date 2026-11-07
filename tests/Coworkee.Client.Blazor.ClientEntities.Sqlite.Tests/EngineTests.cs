using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Client.Blazor.ClientEntities.Sqlite.Tests;

// SQLite where the in-memory copy would keep JSON rows; simple rows stay columns unless every row type is asked for
public sealed class EngineTests
{
    public sealed record Simple(Guid Id, string Name, DateTimeOffset At, Nested? Customer);

    public sealed record Nested(Guid Id, string Name);

    public sealed record WithList(Guid Id, string Name, List<string> Tags);

    [Fact]
    public async Task By_default_only_rows_the_memory_copy_keeps_as_json_go_to_sqlite()
    {
        var engine = Engine(null);

        engine.Open<Simple>("simple").ShouldBeNull();
        await using var store = engine.Open<WithList>("list");
        store.ShouldNotBeNull();
    }

    [Fact]
    public async Task All_row_types_go_to_sqlite_when_asked()
    {
        var engine = Engine(o => o.AllRowTypes = true);

        await using var store = engine.Open<Simple>("simple");
        store.ShouldNotBeNull();
    }

    private static IClientEntityEngine Engine(Action<SqliteClientEntityOptions>? configure)
    {
        var services = new ServiceCollection().AddLogging().AddSingleton<IClientEntityStorage, NoClientEntityStorage>();
        services.AddCoworkeeClientEntitiesSqlite(configure);
        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IClientEntityEngine>();
    }
}
