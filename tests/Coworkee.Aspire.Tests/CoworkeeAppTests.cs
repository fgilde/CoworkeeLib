using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Coworkee.Aspire.Settings;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Theming;
using Microsoft.Extensions.Logging.Abstractions;

namespace Coworkee.Aspire.Tests;

public sealed class CoworkeeAppTests : IDisposable
{
    private readonly List<string> _folders = [];

    public void Dispose() => _folders.ForEach(f => Directory.Delete(f, recursive: true));

    private string Folder()
    {
        var folder = Directory.CreateTempSubdirectory("coworkee-aspire-").FullName;
        _folders.Add(folder);
        return folder;
    }

    [Fact]
    public void Setting_paths_follow_the_configuration_tree()
    {
        SettingPath.Of(s => s.Coworkee.Bff.Scopes[0]).ShouldBe("Coworkee__Bff__Scopes__0");
        SettingPath.Of(s => s.Coworkee.Settings.Defaults["Mail.Smtp.Host"]).ShouldBe("Coworkee__Settings__Defaults__Mail.Smtp.Host");
        SettingPath.Of(s => s.Coworkee.Auth.External.Providers["keycloak"].Authority).ShouldBe("Coworkee__Auth__External__Providers__keycloak__Authority");
        SettingPath.Of(s => s.Coworkee.Jobs.RunServer).ShouldBe("Coworkee__Jobs__RunServer");
    }

    [Fact]
    public async Task A_local_run_gives_every_service_one_setup_token()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo");
        var auth = app.AddAuthServer<FakeProjects.Auth>();
        var api = app.AddApi<FakeProjects.Api>();

        var token = (await Environment(api.Resource))["Coworkee__SetupToken"];

        token.ShouldNotBeNullOrWhiteSpace();
        (await Environment(auth.Resource))["Coworkee__SetupToken"].ShouldBe(token);
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
    public async Task The_migrations_get_the_modules_they_use_like_the_api()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo");

        var migrations = app.AddMigrations<FakeProjects.SeedingMigrations>();
        app.AddAuthServer<FakeProjects.Auth>();
        var api = app.AddApi<FakeProjects.Api>();

        var settings = await Environment(migrations.Resource);
        settings["Coworkee__Storage__FileSystem__Root"].ShouldBe((await Environment(api.Resource))["Coworkee__Storage__FileSystem__Root"]);
        settings.Keys.ShouldNotContain("ConnectionStrings__redis");
        migrations.Resource.Annotations.OfType<WaitAnnotation>().Select(w => w.Resource.Name).ShouldNotContain("demo-migrations");
    }

    [Fact]
    public async Task The_airworks_client_is_a_second_bff_client_of_the_auth_server_with_its_own_cookie()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo", o => o.DisplayName = "Demo");

        app.AddMigrations<FakeProjects.Migrations>();
        var auth = app.AddAuthServer<FakeProjects.Auth>();
        app.AddApi<FakeProjects.Api>();
        app.AddWeb<FakeProjects.Web>();
        var airWorks = app.AddAirWorks<FakeProjects.Web>();

        airWorks.Resource.Name.ShouldBe("demo-airworks");
        app.AirWorks.ShouldBe(airWorks);
        var settings = await Environment(auth.Resource);
        settings["Coworkee__Auth__Clients__0__ClientId"].ShouldBe("demo-web");
        settings["Coworkee__Auth__Clients__1__ClientId"].ShouldBe("demo-airworks");
        settings["Coworkee__Auth__Clients__1__DisplayName"].ShouldBe("Demo (AirWorks)");
        settings["Coworkee__Auth__Clients__0__AllowImpersonation"].ShouldBe("true");
        settings.ShouldNotContainKey("Coworkee__Auth__Clients__1__AllowImpersonation");
        settings["Coworkee__Auth__Clients__1__Scopes__0"].ShouldBe("demo_api");
        settings["Coworkee__Auth__Clients__1__RedirectUris__0"].ShouldBe("{demo-airworks.bindings.https.url}/signin-oidc");
        settings["Coworkee__Auth__Clients__1__PostLogoutRedirectUris__0"].ShouldBe("{demo-airworks.bindings.https.url}/signout-callback-oidc");
        var bff = await Environment(airWorks.Resource);
        bff["Coworkee__Bff__ClientId"].ShouldBe("demo-airworks");
        bff["Coworkee__Bff__ApiAddress"].ShouldBe("https+http://demo-api");
        bff["Coworkee__Bff__Scopes__0"].ShouldBe("demo_api");
        bff["Coworkee__Bff__CookieName"].ShouldBe("demo-airworks");
        bff["Coworkee__Bff__WebAppUrl"].ShouldBe("{demo-web.bindings.https.url}");
        bff["ConnectionStrings__redis"].ShouldNotBeNullOrEmpty();
        var web = await Environment(app.Web!.Resource);
        web.Keys.ShouldNotContain("Coworkee__Bff__CookieName");
        web["Coworkee__Bff__FrameAncestors__0"].ShouldBe("{demo-airworks.bindings.https.url}");
    }

    [Fact]
    public async Task Elsa_studio_is_a_client_of_the_auth_server_that_calls_the_elsa_api_and_the_designer_link_leads_there()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo", o => o.DisplayName = "Demo");

        app.AddMigrations<FakeProjects.Migrations>();
        var auth = app.AddAuthServer<FakeProjects.Auth>();
        var api = app.AddApi<FakeProjects.Api>();
        app.AddWeb<FakeProjects.Web>();
        var studio = app.AddElsaStudio<FakeProjects.Web>();

        studio.Resource.Name.ShouldBe("demo-elsastudio");
        app.ElsaStudio.ShouldBe(studio);
        var settings = await Environment(auth.Resource);
        settings["Coworkee__Auth__Clients__1__ClientId"].ShouldBe("demo-elsastudio");
        settings["Coworkee__Auth__Clients__1__DisplayName"].ShouldBe("Demo (Workflow designer)");
        settings["Coworkee__Auth__Clients__1__Scopes__0"].ShouldBe("demo_api");
        settings["Coworkee__Auth__Clients__1__RedirectUris__0"].ShouldBe("{demo-elsastudio.bindings.https.url}/signin-oidc");
        var host = await Environment(studio.Resource);
        host["Coworkee__Bff__ClientId"].ShouldBe("demo-elsastudio");
        host["Coworkee__Bff__CookieName"].ShouldBe("demo-elsastudio");
        host["Coworkee__Workflows__Elsa__Studio__BackendUrl"].ShouldBe("{demo-api.bindings.https.url}/api/elsa");
        (await Environment(api.Resource))["Coworkee__Workflows__Elsa__StudioUrl"].ShouldBe("{demo-elsastudio.bindings.https.url}");
    }

    [Fact]
    public void Elsa_studio_is_a_tool_a_deployment_can_leave_out()
    {
        var app = DistributedApplication.CreateBuilder(["--operation", "publish", $"--{CoworkeeApp.DeployToolsSetting}:elsastudio=false"]).AddCoworkeeApp("demo");

        CoworkeeTools.All.ShouldContain(CoworkeeTools.ElsaStudio);
        app.Deploys(CoworkeeTools.ElsaStudio).ShouldBeFalse();
    }

    [Fact]
    public async Task A_spa_client_is_a_public_client_at_its_own_address_with_scope_aliases_and_its_origin_in_the_cors_policies()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo", o => o.DisplayName = "Demo");
        var auth = app.AddAuthServer<FakeProjects.Auth>();
        var api = app.AddApi<FakeProjects.Api>();
        app.AddWeb<FakeProjects.Web>();
        var spa = builder.AddContainer("realtime", "node").WithHttpEndpoint(port: 4200, name: "http");

        app.AddSpaClient("realtime", spa, scopes: ["files"], apiScopeAliases: ["Shop"], allowImpersonation: true).ShouldBe(spa);

        var settings = await Environment(auth.Resource);
        settings["Coworkee__Auth__Clients__1__ClientId"].ShouldBe("realtime");
        settings["Coworkee__Auth__Clients__1__DisplayName"].ShouldBe("Demo (realtime)");
        settings.ShouldNotContainKey("Coworkee__Auth__Clients__1__ClientSecret");
        settings["Coworkee__Auth__Clients__1__RedirectUris__0"].ShouldBe("{realtime.bindings.http.url}");
        settings["Coworkee__Auth__Clients__1__PostLogoutRedirectUris__0"].ShouldBe("{realtime.bindings.http.url}");
        settings["Coworkee__Auth__Clients__1__ClientUri"].ShouldBe("{realtime.bindings.http.url}");
        settings["Coworkee__Auth__Clients__1__AllowImpersonation"].ShouldBe("true");
        settings["Coworkee__Auth__Clients__1__Scopes__0"].ShouldBe("demo_api");
        settings["Coworkee__Auth__Clients__1__Scopes__1"].ShouldBe("files");
        settings["Coworkee__Auth__Clients__1__Scopes__2"].ShouldBe("Shop");
        settings["Coworkee__Auth__ApiScopes__Shop"].ShouldBe("demo_api");
        settings["Coworkee__Cors__Origins__0"].ShouldBe("{realtime.bindings.http.url}");
        (await Environment(api.Resource))["Coworkee__Cors__Origins__0"].ShouldBe("{realtime.bindings.http.url}");
    }

    [Fact]
    public async Task A_deployed_spa_client_takes_its_address_from_a_parameter()
    {
        var builder = DistributedApplication.CreateBuilder(["--operation", "publish"]);
        var app = builder.AddCoworkeeApp("demo");
        var auth = app.AddAuthServer<FakeProjects.Auth>();
        var api = app.AddApi<FakeProjects.Api>();
        var spa = builder.AddContainer("realtime", "node").WithHttpEndpoint(port: 4200, name: "http");

        app.AddSpaClient("realtime", spa, redirectPath: "/callback");

        var settings = await Environment(auth.Resource);
        settings["Coworkee__Auth__Clients__0__RedirectUris__0"].ShouldBe("{demo-realtime-url.value}/callback");
        settings["Coworkee__Cors__Origins__0"].ShouldBe("{demo-realtime-url.value}");
        settings.ShouldNotContainKey("Coworkee__Auth__Clients__0__AllowImpersonation");
        (await Environment(api.Resource))["Coworkee__Cors__Origins__0"].ShouldBe("{demo-realtime-url.value}");
    }

    [Fact]
    public async Task Apis_added_after_a_spa_client_allow_its_origin_too()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo");
        app.AddAuthServer<FakeProjects.Auth>();
        app.AddApi<FakeProjects.Api>();
        app.AddSpaClient("one", builder.AddContainer("one", "node").WithHttpEndpoint(port: 4201, name: "http"));

        var later = app.AddApi<FakeProjects.Api>("reports");
        app.AddSpaClient("two", builder.AddContainer("two", "node").WithHttpEndpoint(port: 4202, name: "http"));

        var settings = await Environment(later.Resource);
        settings["Coworkee__Cors__Origins__0"].ShouldBe("{one.bindings.http.url}");
        settings["Coworkee__Cors__Origins__1"].ShouldBe("{two.bindings.http.url}");
        (await Environment(app.AuthServer!.Resource))["Coworkee__Cors__Origins__1"].ShouldBe("{two.bindings.http.url}");
    }

    [Fact]
    public void A_spa_client_needs_the_api()
    {
        var builder = DistributedApplication.CreateBuilder();
        var app = builder.AddCoworkeeApp("demo");
        app.AddAuthServer<FakeProjects.Auth>();

        Should.Throw<InvalidOperationException>(() => app.AddSpaClient("spa", builder.AddContainer("spa", "node").WithHttpEndpoint(name: "http")))
            .Message.ShouldContain("AddApi");
    }

    [Fact]
    public void The_airworks_client_needs_the_api()
    {
        var app = DistributedApplication.CreateBuilder().AddCoworkeeApp("demo");
        app.AddAuthServer<FakeProjects.Auth>();

        Should.Throw<InvalidOperationException>(() => app.AddAirWorks<FakeProjects.Web>()).Message.ShouldContain("AddApi");
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
        var folder = Folder();
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
        var folder = Folder();
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
    public async Task A_login_theme_that_cannot_be_written_is_logged_and_does_not_stop_the_realm()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { ProjectDirectory = Folder() });
        var app = builder.AddCoworkeeApp("realm", o => o.UseKeycloak());
        app.AddMigrations<FakeProjects.Migrations>();
        app.AddAuthServer<FakeProjects.Auth>();
        File.WriteAllText(Path.Combine(app.KeycloakThemes, "coworkee-realm"), "in the way");

        await app.TryWriteLoginThemeAsync(NullLogger.Instance, TestContext.Current.CancellationToken);

        KeycloakLoginTheme.Exists(app.KeycloakThemes, "coworkee-realm").ShouldBeFalse();
    }
    [Fact]
    public async Task Without_the_login_theme_keycloak_mounts_nothing_and_the_api_gets_no_theme_folder()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { ProjectDirectory = Folder() });
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
