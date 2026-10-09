using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Coworkee.Aspire.Tests;

public sealed class DashboardTests
{
    [Fact]
    public void Database_commands_are_offered_in_development_only()
    {
        Commands(DistributedApplication.CreateBuilder(["--environment", "Development"])).ShouldBe([CoworkeeApp.RerunMigrationsCommand, CoworkeeApp.ResetDatabaseCommand], ignoreOrder: true);
        Commands(DistributedApplication.CreateBuilder(["--environment", "Production"])).ShouldBeEmpty();
    }

    [Fact]
    public void Reset_asks_for_confirmation_and_is_not_offered_for_a_supplied_database()
    {
        var builder = DistributedApplication.CreateBuilder(["--environment", "Development"]);
        var app = builder.AddCoworkeeApp("demo");
        app.AddMigrations<FakeProjects.Migrations>().Resource.Annotations.OfType<ResourceCommandAnnotation>()
            .Single(c => c.Name == CoworkeeApp.ResetDatabaseCommand).ConfirmationMessage.ShouldNotBeNullOrEmpty();

        var supplied = DistributedApplication.CreateBuilder(["--environment", "Development"]);
        var database = supplied.AddConnectionString("db");
        Commands(supplied, o => o.UseDatabase(database)).ShouldBe([CoworkeeApp.RerunMigrationsCommand]);
    }

    [Fact]
    public async Task Web_links_to_swagger_and_the_jobs_dashboard()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo").AddProjects();
        var web = app.Web!.Resource;
        var https = web.Annotations.OfType<EndpointAnnotation>().Single(e => e.Name == "https");
        https.AllocatedEndpoint = new AllocatedEndpoint(https, "localhost", 7001);

        var urls = new List<ResourceUrlAnnotation>();
        var context = new ResourceUrlsCallbackContext(builder.ExecutionContext, web, urls, TestContext.Current.CancellationToken);
        foreach (var callback in web.Annotations.OfType<ResourceUrlsCallbackAnnotation>())
        {
            await callback.Callback(context);
        }

        urls.Select(u => u.Url).ShouldBe(["https://localhost:7001/swagger", "https://localhost:7001/admin/jobs"], ignoreOrder: true);
    }

    private static IEnumerable<string> Commands(IDistributedApplicationBuilder builder, Action<CoworkeeAppOptions>? configure = null) =>
        builder.AddCoworkeeApp("demo", configure).AddMigrations<FakeProjects.Migrations>().Resource.Annotations.OfType<ResourceCommandAnnotation>()
            .Select(c => c.Name).Where(n => n.StartsWith("coworkee-", StringComparison.Ordinal));
}
