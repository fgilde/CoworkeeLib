using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Coworkee.Aspire.Settings;

namespace Coworkee.Aspire.Tests;

public sealed class CoworkeeAppTests
{
    [Fact]
    public void Setting_paths_follow_the_configuration_tree()
    {
        SettingPath.Of(s => s.Coworkee.Bff.Scopes[0]).ShouldBe("Coworkee__Bff__Scopes__0");
        SettingPath.Of(s => s.Coworkee.Settings.Defaults["Mail.Smtp.Host"]).ShouldBe("Coworkee__Settings__Defaults__Mail.Smtp.Host");
        SettingPath.Of(s => s.Coworkee.Auth.External.Providers["keycloak"].Authority).ShouldBe("Coworkee__Auth__External__Providers__keycloak__Authority");
        SettingPath.Of(s => s.Coworkee.Jobs.RunServer).ShouldBe("Coworkee__Jobs__RunServer");
    }

    [Fact]
    public async Task Services_get_the_infrastructure_of_their_modules_and_the_settings_that_connect_them()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo", o => o.DisplayName = "Demo");

        app.AddMigrations<FakeProjects.Migrations>();
        var auth = app.AddAuthServer<FakeProjects.Auth>();
        var api = app.AddApi<FakeProjects.Api>();
        var web = app.AddWeb<FakeProjects.Web>();

        builder.Resources.Select(r => r.Name).Where(n => !n.EndsWith("-rebuilder", StringComparison.Ordinal)).ShouldBe(["postgres", "demo", "demo-migrations", "demo-auth", "demo-api", "redis", "mail", "demo-web"], ignoreOrder: true);
        var apiSettings = await Environment(api.Resource);
        apiSettings.Keys.ShouldContain("Coworkee__ApiAuth__Authority");
        apiSettings.Keys.ShouldContain("Coworkee__Settings__Defaults__Mail.Smtp.Host");
        apiSettings.Keys.ShouldContain("Coworkee__Storage__FileSystem__Root");
        apiSettings.Keys.ShouldContain("ConnectionStrings__redis");
        apiSettings.Keys.ShouldNotContain("ConnectionStrings__elasticsearch");
        (await Environment(auth.Resource))["Coworkee__Auth__Clients__0__ClientId"].ShouldBe("demo-web");
        (await Environment(auth.Resource))["Coworkee__Auth__ApiScopes__demo_api"].ShouldBe("demo_api");
        (await Environment(web.Resource))["Coworkee__Bff__ApiAddress"].ShouldBe("https+http://demo-api");
        api.Resource.Annotations.OfType<WaitAnnotation>().Select(w => w.Resource.Name).ShouldContain("demo-migrations");
    }

    [Fact]
    public async Task Keycloak_is_imported_with_a_realm_for_the_app_and_offered_at_the_auth_server()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo", o => o.UseKeycloak(k => k.Users.Add(new KeycloakUser("ada@demo.test", "Ada", "Lovelace"))));

        app.AddMigrations<FakeProjects.Migrations>();
        var auth = app.AddAuthServer<FakeProjects.Auth>();

        app.Keycloak.ShouldNotBeNull();
        var settings = await Environment(auth.Resource);
        settings["Coworkee__Auth__External__Providers__keycloak__ClientId"].ShouldBe("demo-auth");
        settings["Coworkee__Auth__External__Mode"].ShouldBe("Both");
        var realm = await File.ReadAllTextAsync(Path.Combine(builder.AppHostDirectory, "obj", "keycloak", "demo-realm.json"), TestContext.Current.CancellationToken);
        realm.ShouldContain("ada@demo.test");
        realm.ShouldContain("${COWORKEE_KEYCLOAK_CLIENT_SECRET}");
    }

    [Fact]
    public void Order_mistakes_are_explained()
    {
        var app = DistributedApplication.CreateBuilder().AddCoworkeeApp("demo");

        Should.Throw<InvalidOperationException>(() => app.AddWeb<FakeProjects.Web>()).Message.ShouldContain("AddAuthServer");
    }

    private static async Task<Dictionary<string, string?>> Environment(IResourceWithEnvironment resource) =>
        await resource.GetEnvironmentVariableValuesAsync(DistributedApplicationOperation.Publish);
}
