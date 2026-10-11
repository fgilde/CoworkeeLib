using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Coworkee.Aspire.Tests;

public sealed class DatabaseProviderTests
{
    [Fact]
    public async Task Postgres_is_the_default_provider()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo");

        app.AddProjects();

        builder.Resources.Select(r => r.Name).ShouldContain("postgres");
        (await Environment(app.Apis[0].Resource))["Coworkee__Database__Provider"].ShouldBe("Postgres");
    }

    [Fact]
    public async Task Sql_server_replaces_the_postgres_container()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo", o => o.UseSqlServer(s => s.WithEnvironment("MSSQL", "1")));

        app.AddProjects();

        builder.Resources.Select(r => r.Name).ShouldNotContain("postgres");
        (await Environment(app.SqlServer.Resource))["MSSQL"].ShouldBe("1");
        foreach (var service in new IResourceWithEnvironment[] { app.Migrations!.Resource, app.Apis[0].Resource })
        {
            var environment = await Environment(service);
            environment["Coworkee__Database__Provider"].ShouldBe("SqlServer");
            environment["ConnectionStrings__demo"].ShouldBe("{demo.connectionString}");
        }
    }

    [Fact]
    public async Task Sqlite_is_a_file_without_a_container()
    {
        var builder = DistributedApplication.CreateBuilder();
        var file = Path.Combine(Path.GetTempPath(), "coworkee-aspire-test", "demo.db");
        var app = builder.AddCoworkeeApp("demo", o => o.UseSqlite(file));

        app.AddProjects();

        builder.Resources.Select(r => r.Name).ShouldNotContain("postgres");
        builder.Resources.Select(r => r.Name).ShouldNotContain("sqlserver");
        var api = await Environment(app.Apis[0].Resource);
        api["Coworkee__Database__Provider"].ShouldBe("Sqlite");
        api["ConnectionStrings__demo"].ShouldBe($"Data Source={file}");
    }

    private static async Task<Dictionary<string, string>> Environment(IResourceWithEnvironment resource) =>
        await resource.GetEnvironmentVariableValuesAsync(DistributedApplicationOperation.Publish);
}
