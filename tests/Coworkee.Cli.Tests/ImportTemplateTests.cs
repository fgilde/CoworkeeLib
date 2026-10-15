using Coworkee.Cli.Commands;
using Coworkee.Cli.Spec;
using Coworkee.Cli.Spec.Import;
using Microsoft.Data.Sqlite;

namespace Coworkee.Cli.Tests;

public sealed class ImportTemplateTests(TemplateFixture template)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Entities_imported_from_a_database_and_a_csv_file_build_with_the_app()
    {
        var feed = Environment.GetEnvironmentVariable("COWORKEE_TEST_FEED");
        Assert.SkipWhen(string.IsNullOrEmpty(feed), "Set COWORKEE_TEST_FEED to a folder with packed Coworkee packages (build/pack-local.ps1) to build a generated app.");
        var spec = new AppSpec { Name = "Shop", Options = { ["samples"] = false, ["tests"] = false } };
        using var assets = new TempFolder();
        var settings = new NewSettings(spec, assets.Path, Path.Combine(template.Root, "import-build"), null);
        var app = await template.CreateAsync("import-build", NewCommand.TemplateArgs(settings)[7..]);
        await Dotnet.RunAsync(app, "nuget", "add", "source", Path.GetFullPath(feed!), "--name", "local", "--configfile", "nuget.config");
        (await NewCommand.CompleteAsync(settings, Ct)).ShouldBe(0);
        var database = Path.Combine(assets.Path, "legacy.db");
        await using (var connection = new SqliteConnection($"Data Source={database}"))
        {
            await connection.OpenAsync(Ct);
            await using var command = connection.CreateCommand();
            command.CommandText = string.Format(DbImportTests.Schema, "varchar(20)", "blob");
            await command.ExecuteNonQueryAsync(Ct);
        }

        SqliteConnection.ClearAllPools();
        var csv = assets.File("suppliers.csv");
        await File.WriteAllTextAsync(csv, "Name,Email,Rating\nAcme,info@acme.test,4\n", Ct);
        var solution = Solution.Find(app);

        (await EntityImportCommand.ApplyAsync(solution, await EntityImportCommand.ImportAsync(new ImportRequest(ImportSource.Db, database, "Legacy"), Ct), Ct)).ShouldBe(0);
        (await EntityImportCommand.ApplyAsync(solution, await EntityImportCommand.ImportAsync(new ImportRequest(ImportSource.Csv, csv, "Legacy"), Ct), Ct)).ShouldBe(0);
        await Dotnet.RunAsync(app, [.. MigrationsCommand.AddArgs(solution, "ImportLegacy")[1..]]);

        File.Exists(Path.Combine(app, "src/Shop.Legacy/Domain/Order.cs")).ShouldBeTrue();
        File.Exists(Path.Combine(app, "src/Shop.Legacy/Domain/Supplier.cs")).ShouldBeTrue();
        await Dotnet.RunAsync(app, "build", "Shop.slnx");
    }
}
