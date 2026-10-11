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
        var app = builder.AddCoworkeeApp("demo", o =>
        {
            o.DisplayName = "Demo";
            o.LogoUrl = "/coworkee-icon.svg";
        });

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
        (await Environment(auth.Resource))["Coworkee__Auth__LogoUrl"].ShouldEndWith("/coworkee-icon.svg");
        (await Environment(auth.Resource))["Coworkee__Auth__Clients__0__ClientUri"].ShouldNotBeNullOrEmpty();
        (await Environment(web.Resource))["Coworkee__Bff__ApiAddress"].ShouldBe("https+http://demo-api");
        api.Resource.Annotations.OfType<WaitAnnotation>().Select(w => w.Resource.Name).ShouldContain("demo-migrations");
    }

    [Fact]
    public async Task The_sign_in_design_reaches_the_auth_server_with_media_paths_on_the_web_app()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo", o =>
        {
            o.SignIn.Layout = "Split";
            o.SignIn.MediaUrl = "/media/welcome.mp4";
            o.SignIn.MediaSide = Coworkee.Contracts.Theming.SignInMediaSide.Right;
        });

        app.AddMigrations<FakeProjects.Migrations>();
        var auth = app.AddAuthServer<FakeProjects.Auth>();
        app.AddApi<FakeProjects.Api>();
        app.AddWeb<FakeProjects.Web>();

        var settings = await Environment(auth.Resource);
        settings["Coworkee__Auth__Design__Layout"].ShouldBe("Split");
        settings["Coworkee__Auth__Design__MediaSide"].ShouldBe("Right");
        settings["Coworkee__Auth__Design__MediaUrl"].ShouldStartWith("{demo-web.bindings.https.url}");
        settings["Coworkee__Auth__Design__MediaUrl"].ShouldEndWith("/media/welcome.mp4");
    }

    [Fact]
    public async Task Keycloak_gets_a_realm_for_the_app_offered_at_the_auth_server_and_room_for_large_cookies()
    {
        var builder = DistributedApplication.CreateBuilder();
        var ada = new KeycloakUser("ada@demo.test", "Ada", "Lovelace") { Password = "Ada-Password1" };
        var app = builder.AddCoworkeeApp("realm", o => o.UseKeycloak(k => k.Users.AddRange([ada, new KeycloakUser("bob@demo.test", "Bob", "Builder")])));

        app.AddMigrations<FakeProjects.Migrations>();
        var auth = app.AddAuthServer<FakeProjects.Auth>();
        var grafana = app.AddKeycloakClient("realm-grafana", [ReferenceExpression.Create($"https://localhost:3000/login/generic_oauth")]);

        app.Keycloak.ShouldNotBeNull();
        var settings = await Environment(auth.Resource);
        settings["Coworkee__Auth__External__Providers__keycloak__ClientId"].ShouldBe("realm-auth");
        settings["Coworkee__Auth__External__Providers__keycloak__Authority"].ShouldEndWith("/realms/realm");
        settings["Coworkee__Auth__External__Mode"].ShouldBe("Both");
        (await Environment(app.Keycloak.Resource))[CoworkeeApp.KeycloakHeaderLimit].ShouldBe("65536");
        app.KeycloakClients.Select(c => c.ClientId).ShouldBe(["realm-auth", "realm-grafana"]);
        grafana.Secret.Resource.Name.ShouldBe("realm-grafana-secret");
        (await app.KeycloakPassword(ada).GetValueAsync(TestContext.Current.CancellationToken)).ShouldBe("Ada-Password1");
        app.KeycloakPassword(app.KeycloakUsers[1]).ValueProviders.OfType<ParameterResource>().Single().Name.ShouldBe("realm-keycloak-user-password");
    }

    [Fact]
    public void Keycloak_clients_need_keycloak()
    {
        var app = DistributedApplication.CreateBuilder().AddCoworkeeApp("demo");
        app.AddAuthServer<FakeProjects.Auth>();

        Should.Throw<InvalidOperationException>(() => app.AddKeycloakClient("demo-grafana")).Message.ShouldContain("UseKeycloak");
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
