using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Coworkee.Aspire.Tests;

public sealed class ProjectsTests
{
    [Fact]
    public void Adds_the_referenced_projects_by_name_in_role_order()
    {
        var builder = DistributedApplication.CreateBuilder();

        var app = builder.AddCoworkeeApp("demo").AddProjects();

        Projects(builder).ShouldBe(["demo-migrations", "demo-auth", "demo-api", "demo-jobs-worker", "demo-web"]);
        app.Migrations!.Resource.Name.ShouldBe("demo-migrations");
        app.Web!.Resource.Name.ShouldBe("demo-web");
        app.Apis.ShouldHaveSingleItem().Resource.Name.ShouldBe("demo-api");
    }

    [Fact]
    public void Skipped_projects_and_projects_added_before_are_left_out()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo", o => o.Skip("jobs-worker", "Demo_Web"));

        app.AddMigrations<global::Projects.Demo_Migrations>("db");
        app.AddProjects();

        Projects(builder).ShouldBe(["demo-db", "demo-auth", "demo-api"]);
    }

    [Fact]
    public async Task Configure_reaches_a_service_by_suffix()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo", o => o.Configure("api", api => api.WithEnvironment("FEATURE", "on")));

        app.AddProjects();

        (await app.Apis[0].Resource.GetEnvironmentVariableValuesAsync(DistributedApplicationOperation.Publish))["FEATURE"].ShouldBe("on");
    }

    private static IEnumerable<string> Projects(IDistributedApplicationBuilder builder) =>
        builder.Resources.OfType<ProjectResource>().Select(p => p.Name);
}
