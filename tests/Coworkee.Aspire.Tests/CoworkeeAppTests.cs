using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Coworkee.Aspire.Settings;
using Coworkee.Contracts.Configuration;

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
        var api = app.AddApi<FakeProjects.Api>();
        app.AddWeb<FakeProjects.Web>();

        // the api hosts the setting administrators change, so it starts from the same design
        (await Environment(api.Resource))["Coworkee__Auth__Design__MediaUrl"].ShouldBe((await Environment(auth.Resource))["Coworkee__Auth__Design__MediaUrl"]);
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
    public async Task The_api_hosting_the_settings_gets_the_sign_in_mode_and_provider_names_but_no_secrets()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("realm", o => o.UseKeycloak(k => k.LoginMode = LoginMode.External));

        app.AddMigrations<FakeProjects.Migrations>();
        app.AddAuthServer<FakeProjects.Auth>();
        var api = app.AddApi<FakeProjects.Api>();

        var settings = await Environment(api.Resource);
        settings["Coworkee__Auth__External__Mode"].ShouldBe("External");
        settings["Coworkee__Auth__External__Providers__keycloak__DisplayName"].ShouldBe("Keycloak");
        settings.Keys.Where(k => k.StartsWith("Coworkee__Auth__External__")).ShouldBe(
            ["Coworkee__Auth__External__Mode", "Coworkee__Auth__External__Providers__keycloak__DisplayName"], ignoreOrder: true);
    }

    [Fact]
    public async Task Keycloak_gets_the_login_theme_folder_and_the_api_what_it_needs_to_rewrite_the_theme()
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { ProjectDirectory = folder });
        var app = builder.AddCoworkeeApp("realm", o =>
        {
            o.DisplayName = "Realm App";
            o.LogoUrl = "/logo.svg";
            o.SignIn.Layout = "Split";
            o.UseKeycloak();
        });

        app.AddMigrations<FakeProjects.Migrations>();
        app.AddAuthServer<FakeProjects.Auth>();
        var api = app.AddApi<FakeProjects.Api>();
        app.AddWeb<FakeProjects.Web>();

        var themes = Path.Combine(folder, ".coworkee", "keycloak-themes");
        app.KeycloakThemes.ShouldBe(themes);
        Directory.Exists(themes).ShouldBeTrue();
        var mount = app.Keycloak!.Resource.Annotations.OfType<ContainerMountAnnotation>().Single(m => m.Target == "/opt/keycloak/themes");
        mount.Source.ShouldBe(themes);
        mount.IsReadOnly.ShouldBeTrue();
        var settings = await Environment(api.Resource);
        settings["Coworkee__Keycloak__ThemeDirectory"].ShouldBe(themes);
        settings["Coworkee__Keycloak__ThemeName"].ShouldBe("coworkee-realm");
        settings["Coworkee__Keycloak__AppName"].ShouldBe("Realm App");
        settings["Coworkee__Keycloak__LogoUrl"].ShouldBe("/logo.svg");
        settings["Coworkee__Keycloak__BaseUrl"].ShouldBe("{realm-web.bindings.https.url}");
        settings["Coworkee__Auth__Design__Layout"].ShouldBe("Split");
    }

    [Fact]
    public async Task The_login_theme_is_written_once_from_the_options_with_paths_on_the_web_app()
    {
        var folder = Directory.CreateTempSubdirectory().FullName;
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { ProjectDirectory = folder });
        var app = builder.AddCoworkeeApp("realm", o =>
        {
            o.DisplayName = "Realm App";
            o.LogoUrl = "/logo.svg";
            o.SignIn.MediaUrl = "/media/welcome.mp4";
            o.UseKeycloak();
        });
        app.AddMigrations<FakeProjects.Migrations>();
        app.AddAuthServer<FakeProjects.Auth>();
        app.AddApi<FakeProjects.Api>();
        var web = app.AddWeb<FakeProjects.Web>();
        foreach (var endpoint in web.Resource.Annotations.OfType<EndpointAnnotation>())
        {
            endpoint.AllocatedEndpoint = new AllocatedEndpoint(endpoint, "localhost", 7001);
        }

        await app.WriteLoginThemeAsync(TestContext.Current.CancellationToken);

        var login = Path.Combine(app.KeycloakThemes, "coworkee-realm", "login");
        File.ReadAllText(Path.Combine(login, "resources", "css", "coworkee.css")).ShouldContain("url(\"https://localhost:7001/logo.svg\")");
        File.ReadAllText(Path.Combine(login, "resources", "js", "coworkee.js")).ShouldContain("\"media\":\"https://localhost:7001/media/welcome.mp4\"");
        File.ReadAllText(Path.Combine(login, "messages", "messages_en.properties")).ShouldContain("Realm App");

        File.WriteAllText(Path.Combine(login, "messages", "messages_en.properties"), "loginTitleHtml=Changed by the api");
        await app.WriteLoginThemeAsync(TestContext.Current.CancellationToken);
        File.ReadAllText(Path.Combine(login, "messages", "messages_en.properties")).ShouldBe("loginTitleHtml=Changed by the api");
    }

    [Fact]
    public async Task Without_the_login_theme_keycloak_mounts_nothing_and_the_api_gets_no_theme_folder()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { ProjectDirectory = Directory.CreateTempSubdirectory().FullName });
        var app = builder.AddCoworkeeApp("realm", o => o.UseKeycloak(k => k.LoginTheme = false));
        app.AddMigrations<FakeProjects.Migrations>();
        app.AddAuthServer<FakeProjects.Auth>();
        var api = app.AddApi<FakeProjects.Api>();

        app.Keycloak!.Resource.Annotations.OfType<ContainerMountAnnotation>().ShouldNotContain(m => m.Target == "/opt/keycloak/themes");
        (await Environment(api.Resource)).Keys.ShouldNotContain(k => k.StartsWith("Coworkee__Keycloak__"));
        await app.WriteLoginThemeAsync(TestContext.Current.CancellationToken);
        Directory.Exists(app.KeycloakThemes).ShouldBeFalse();
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
