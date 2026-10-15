using Coworkee.Cli.Apps;
using Coworkee.Cli.Commands;

namespace Coworkee.Cli.Tests;

public sealed class DbTests(TemplateFixture template)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("postgres", "postgres", "Host=localhost;Port=5000;Username=postgres;Password=pw;Database=shop")]
    [InlineData("sqlServer", "sqlserver", "Server=localhost,5000;User ID=sa;Password=pw;Database=shop;TrustServerCertificate=true")]
    public async Task The_database_of_an_app_is_found_with_its_volumes_and_connection(string provider, string resource, string connection)
    {
        var database = LocalDatabase.Of(Solution.Find(await template.CreateAsync($"dbcmd-{provider}", "--samples", "false", "--tests", "false", "--database", provider)));

        database.Provider.ShouldBe(provider);
        database.Name.ShouldBe("shop");
        database.Resource.ShouldBe(resource);
        database.ConnectionString(5000, "pw").ShouldBe(connection);
        database.Volumes([$"shop.apphost-0123456789-{resource}-data", "shop.apphost-0123456789-redis-data", $"myshop.apphost-0123456789-{resource}-data", $"shop.apphost-abcdefabcd-{resource}-data"])
            .ShouldBe([$"shop.apphost-0123456789-{resource}-data", $"shop.apphost-abcdefabcd-{resource}-data"]);
    }

    [Fact]
    public async Task Sqlite_backup_and_restore_copy_the_file()
    {
        var database = LocalDatabase.Of(Solution.Find(await template.CreateAsync("dbcmd-sqlite", "--samples", "false", "--tests", "false", "--database", "sqlite")));
        database.SqliteFile.ShouldEndWith(Path.Combine(".data", "shop.db"));
        Directory.CreateDirectory(Path.GetDirectoryName(database.SqliteFile)!);
        File.WriteAllText(database.SqliteFile, "before");
        var backup = Path.Combine(database.Root, "backup.db");

        (await DbCommand.BackupAsync(database, backup, null, Ct)).ShouldBe(0);
        File.WriteAllText(database.SqliteFile, "after");
        File.WriteAllText(database.SqliteFile + "-wal", "journal");
        (await DbCommand.RestoreAsync(database, backup, yes: true, null, Ct)).ShouldBe(0);

        File.ReadAllText(database.SqliteFile).ShouldBe("before");
        File.Exists(database.SqliteFile + "-wal").ShouldBeFalse();
    }
}
