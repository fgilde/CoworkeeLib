using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Nextended.Aspire.Hosting.AspireUI;

namespace Coworkee.Aspire.Tests;

public sealed class DashboardTests
{
    [Fact]
    public void Database_commands_are_offered_in_development_only()
    {
        Commands(DistributedApplication.CreateBuilder(["--environment", "Development"])).ShouldBe([CoworkeeApp.RerunMigrationsCommand, CoworkeeApp.ResetDatabaseCommand], ignoreOrder: true);
        Commands(DistributedApplication.CreateBuilder(["--environment", "Production"])).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    public async Task The_migrations_of_a_local_run_sync_the_development_users(string environment, bool synced)
    {
        var builder = DistributedApplication.CreateBuilder(["--environment", environment]);
        var migrations = builder.AddCoworkeeApp("demo").AddMigrations<FakeProjects.Migrations>();

        var env = new Dictionary<string, object>();
        foreach (var callback in migrations.Resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await callback.Callback(new EnvironmentCallbackContext(builder.ExecutionContext, env, TestContext.Current.CancellationToken));
        }

        env.ContainsKey("Coworkee__Identity__SyncDevelopmentUsers").ShouldBe(synced);
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

    [Fact]
    public async Task Every_service_knows_the_dashboards_and_the_resources_with_a_web_endpoint()
    {
        var builder = DistributedApplication.CreateBuilder(["--environment", "Development"]);
        builder.Configuration["ASPNETCORE_URLS"] = "http://localhost:15000;https://localhost:17000";
        var app = builder.AddCoworkeeApp("demo").AddProjects();
        builder.AddContainer("pgadmin", "dpage/pgadmin4").WithHttpEndpoint(targetPort: 80);

        var env = new Dictionary<string, object>();
        foreach (var callback in app.Apis[0].Resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await callback.Callback(new EnvironmentCallbackContext(builder.ExecutionContext, env, TestContext.Current.CancellationToken));
        }

        env["Coworkee__Services__dashboard__Url"].ShouldBe("https://localhost:17000");
        env["Coworkee__Services__jobs__Url"].ShouldBeOfType<ReferenceExpression>();
        env["Coworkee__Services__demo-web__Url"].ShouldBeOfType<EndpointReference>().EndpointName.ShouldBe("https");
        env["Coworkee__Services__demo-api__HealthPath"].ShouldBe("/health");
        env["Coworkee__Services__mail__Url"].ShouldBeOfType<EndpointReference>().EndpointName.ShouldBe("http");
        env.Keys.ShouldContain("Coworkee__Services__pgadmin__Url");
        env.Keys.ShouldNotContain("Coworkee__Services__mail__HealthPath");
        env.Keys.ShouldNotContain("Coworkee__Services__postgres__Url");
    }

    [Fact]
    public async Task An_aspireui_with_the_retrieve_api_is_the_source_of_the_service_directory()
    {
        var builder = DistributedApplication.CreateBuilder(["--environment", "Development"]);
        var app = builder.AddCoworkeeApp("demo").AddProjects();
        builder.AddAspireUI().WithRetrieveApi();

        var env = await ApiEnvironment(builder, app);

        env["Coworkee__AspireUI__Url"].ShouldBeOfType<EndpointReference>().EndpointName.ShouldBe("http");
        env.Keys.ShouldNotContain("Coworkee__AspireUI__ApiKey");
    }

    [Fact]
    public async Task The_key_of_the_retrieve_api_stays_a_parameter_reference()
    {
        var builder = DistributedApplication.CreateBuilder(["--environment", "Development"]);
        var app = builder.AddCoworkeeApp("demo").AddProjects();
        var key = builder.AddParameter("aspireui-key", secret: true);
        builder.AddAspireUI().WithRetrieveApi(key);

        var env = await ApiEnvironment(builder, app);

        env["Coworkee__AspireUI__ApiKey"].ShouldBe(key.Resource);
    }

    [Fact]
    public async Task An_aspireui_without_the_retrieve_api_is_left_out()
    {
        var builder = DistributedApplication.CreateBuilder(["--environment", "Development"]);
        var app = builder.AddCoworkeeApp("demo").AddProjects();
        builder.AddAspireUI();

        (await ApiEnvironment(builder, app)).Keys.ShouldNotContain("Coworkee__AspireUI__Url");
    }

    [Fact]
    public async Task The_first_aspireui_with_the_retrieve_api_wins()
    {
        var builder = DistributedApplication.CreateBuilder(["--environment", "Development"]);
        var app = builder.AddCoworkeeApp("demo").AddProjects();
        var first = builder.AddAspireUI("first").WithRetrieveApi();
        builder.AddAspireUI("second").WithRetrieveApi("secret");

        var env = await ApiEnvironment(builder, app);

        env["Coworkee__AspireUI__Url"].ShouldBeOfType<EndpointReference>().Resource.ShouldBe(first.Resource);
        env.Keys.ShouldNotContain("Coworkee__AspireUI__ApiKey");
    }

    [Fact]
    public async Task An_aspireui_with_the_sign_on_of_the_app_is_still_the_source_of_the_service_directory()
    {
        var builder = DistributedApplication.CreateBuilder(["--environment", "Development"]);
        var app = builder.AddCoworkeeApp("demo", o => o.UseKeycloak()).AddProjects();
        foreach (var endpoint in app.Keycloak!.Resource.Annotations.OfType<EndpointAnnotation>())
        {
            endpoint.AllocatedEndpoint ??= new AllocatedEndpoint(endpoint, "localhost", 8180);
        }

        IResource? seen = null;
        var aspireUi = builder.AddAspireUI().WithCoworkeeSignOn(app).WithRetrieveApi().WithEnvironment(context => seen = context.Resource);

        var env = await ApiEnvironment(builder, app);

        env["Coworkee__AspireUI__Url"].ShouldBeOfType<EndpointReference>().EndpointName.ShouldBe("http");
        seen.ShouldBe(aspireUi.Resource);
    }

    [Fact]
    public async Task A_throwing_callback_of_an_aspireui_does_not_break_the_environment_of_the_app()
    {
        var builder = DistributedApplication.CreateBuilder(["--environment", "Development"]);
        var app = builder.AddCoworkeeApp("demo").AddProjects();
        builder.AddAspireUI().WithRetrieveApi().WithEnvironment(_ => throw new InvalidOperationException("foreign callback"));

        var env = await ApiEnvironment(builder, app);

        env.Keys.ShouldContain("Coworkee__Services__jobs__Url");
        env.Keys.ShouldNotContain("Coworkee__AspireUI__Url");
    }

    [Fact]
    public async Task Published_apps_get_no_aspireui()
    {
        var builder = DistributedApplication.CreateBuilder(["--operation", "publish"]);
        var app = builder.AddCoworkeeApp("demo").AddProjects();
        builder.AddAspireUI().WithRetrieveApi();

        (await ApiEnvironment(builder, app)).Keys.ShouldNotContain("Coworkee__AspireUI__Url");
    }

    private static async Task<Dictionary<string, object>> ApiEnvironment(IDistributedApplicationBuilder builder, CoworkeeApp app)
    {
        var env = new Dictionary<string, object>();
        foreach (var callback in app.Apis[0].Resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await callback.Callback(new EnvironmentCallbackContext(builder.ExecutionContext, env, TestContext.Current.CancellationToken));
        }

        return env;
    }

    private static IEnumerable<string> Commands(IDistributedApplicationBuilder builder, Action<CoworkeeAppOptions>? configure = null) =>
        builder.AddCoworkeeApp("demo", configure).AddMigrations<FakeProjects.Migrations>().Resource.Annotations.OfType<ResourceCommandAnnotation>()
            .Select(c => c.Name).Where(n => n.StartsWith("coworkee-", StringComparison.Ordinal));
}
