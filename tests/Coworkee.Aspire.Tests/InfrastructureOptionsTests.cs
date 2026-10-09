using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Coworkee.Aspire.Modules;

namespace Coworkee.Aspire.Tests;

public sealed class InfrastructureOptionsTests
{
    [Fact]
    public async Task Supplied_database_and_redis_replace_the_containers()
    {
        var builder = DistributedApplication.CreateBuilder();
        var database = builder.AddConnectionString("production-db");
        var redis = builder.AddConnectionString("production-redis");
        var app = builder.AddCoworkeeApp("demo", o => o.UseDatabase(database).UseRedis(redis));

        app.AddProjects();

        builder.Resources.Select(r => r.Name).ShouldNotContain("postgres");
        builder.Resources.Select(r => r.Name).ShouldNotContain("redis");
        var api = await Environment(app.Apis[0].Resource);
        api["ConnectionStrings__demo"].ShouldBe("{production-db.connectionString}");
        api["ConnectionStrings__redis"].ShouldBe("{production-redis.connectionString}");
    }

    [Fact]
    public async Task Supplied_mail_server_and_keycloak_replace_the_containers()
    {
        var builder = DistributedApplication.CreateBuilder();
        var smtp = builder.AddContainer("smtp", "example/smtp").WithEndpoint(targetPort: 25, name: "smtp");
        var keycloak = builder.AddKeycloak("idp");
        var app = builder.AddCoworkeeApp("demo", o => o.UseMail(smtp.GetEndpoint("smtp")).UseKeycloak(keycloak));

        app.AddProjects();

        builder.Resources.Select(r => r.Name).ShouldNotContain("mail");
        builder.Resources.Select(r => r.Name).ShouldNotContain("keycloak");
        app.Keycloak!.Resource.Name.ShouldBe("idp");
        (await Environment(app.AuthServer!.Resource))["Coworkee__Settings__Defaults__Mail.Smtp.Host"].ShouldBe("{smtp.bindings.smtp.host}");
    }

    [Fact]
    public async Task Configure_callbacks_reach_the_containers()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo", o => o.ConfigurePostgres(p => p.WithEnvironment("PG", "1")).ConfigureRedis(r => r.WithEnvironment("REDIS", "1")));

        app.AddProjects();

        (await Environment(app.Server.Resource))["PG"].ShouldBe("1");
        (await Environment(app.Redis.Resource))["REDIS"].ShouldBe("1");
    }

    [Fact]
    public async Task Switched_off_modules_get_no_infrastructure()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo", o => o.Without(CoworkeeModules.Search, CoworkeeModules.Realtime));

        app.AddProjects();

        builder.Resources.Select(r => r.Name).ShouldNotContain("elasticsearch");
        builder.Resources.Select(r => r.Name).ShouldNotContain("redis");
        (await Environment(app.Apis[0].Resource)).Keys.ShouldNotContain("ConnectionStrings__elasticsearch");
    }

    [Fact]
    public async Task App_modules_are_wired_like_the_built_in_ones()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo");
        var stripe = builder.AddConnectionString("stripe");

        app.Modules.Add("Demo.Billing", (_, service) => service.WithReference(stripe));
        app.AddProjects();

        (await Environment(app.Apis[0].Resource)).Keys.ShouldContain("ConnectionStrings__stripe");
        (await Environment(app.Web!.Resource)).Keys.ShouldNotContain("ConnectionStrings__stripe");
    }

    [Fact]
    public async Task Notification_links_point_to_the_web_app_added_later()
    {
        var builder = DistributedApplication.CreateBuilder();

        builder.AddCoworkeeApp("demo").AddProjects();

        var worker = builder.Resources.OfType<ProjectResource>().Single(p => p.Name == "demo-jobs-worker");
        (await Environment(worker))["Coworkee__Notifications__PublicAppUrl"].ShouldBe("{demo-web.bindings.https.url}");
    }

    private static async Task<Dictionary<string, string>> Environment(IResourceWithEnvironment resource) =>
        await resource.GetEnvironmentVariableValuesAsync(DistributedApplicationOperation.Publish);
}
