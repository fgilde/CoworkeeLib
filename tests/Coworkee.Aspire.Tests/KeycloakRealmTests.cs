using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Aspire.Hosting;
using Coworkee.Contracts.Theming;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Coworkee.Aspire.Tests;

/// <summary>The realm reconcile and the header limit against a real Keycloak, configured like the app host does it.</summary>
public sealed class KeycloakRealmTests : IAsyncLifetime
{
    private const string Admin = "admin";
    private const string AdminPassword = "admin-password";
    private static readonly string Themes = Directory.CreateTempSubdirectory("keycloak-themes-").FullName;

    private readonly IContainer _keycloak = new ContainerBuilder("quay.io/keycloak/keycloak:26.6")
        .WithCommand("start-dev")
        .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", Admin)
        .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", AdminPassword)
        .WithEnvironment("KC_HEALTH_ENABLED", "true")
        .WithEnvironment(CoworkeeApp.KeycloakHeaderLimit, "65536")
        .WithPortBinding(8080, true)
        .WithPortBinding(9000, true)
        .WithBindMount(Themes, "/opt/keycloak/themes")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(9000).ForPath("/health/ready")))
        .Build();

    private Uri Base => new($"http://{_keycloak.Hostname}:{_keycloak.GetMappedPublicPort(8080)}/");

    public async ValueTask InitializeAsync() => await _keycloak.StartAsync();

    public async ValueTask DisposeAsync() => await _keycloak.DisposeAsync();

    [Fact]
    public async Task A_missing_realm_is_created_and_an_existing_one_gets_changed_secrets_and_passwords()
    {
        var ct = TestContext.Current.CancellationToken;
        var ada = new KeycloakUser("ada@demo.test", "Ada", "Lovelace") { Password = "first-Password1" };

        await Reconcile([new("demo-auth", "first-secret", [])], [ada], ct);
        (await SignInAsync(ada.Email, "first-Password1", ct)).ShouldBe(HttpStatusCode.OK);

        var grace = new KeycloakUser("grace@demo.test", "Grace", "Hopper") { Password = "grace-Password1" };
        await Reconcile([new("demo-auth", "second-secret", []), new("demo-grafana", "grafana-secret", ["https://localhost:3000/login/generic_oauth"])],
            [ada with { Password = "second-Password1" }, grace], ct);

        (await SignInAsync(ada.Email, "first-Password1", ct)).ShouldNotBe(HttpStatusCode.OK);
        (await SignInAsync(ada.Email, "second-Password1", ct)).ShouldBe(HttpStatusCode.OK);
        (await SignInAsync(grace.Email, "grace-Password1", ct)).ShouldBe(HttpStatusCode.OK);
        (await SecretAsync("demo-auth", ct)).ShouldBe("second-secret");
        (await SecretAsync("demo-grafana", ct)).ShouldBe("grafana-secret");
    }

    [Fact]
    public async Task Realm_roles_follow_the_users_and_every_client_puts_them_in_the_roles_claim()
    {
        var ct = TestContext.Current.CancellationToken;
        var ada = new KeycloakUser("ada@demo.test", "Ada", "Lovelace") { Password = "ada-Password1", Roles = ["Administrator"] };
        await Reconcile([new("demo-auth", "secret", [])], [ada], ct);
        (await RealmRolesAsync(ada.Email, ct)).ShouldContain("Administrator");

        var grace = new KeycloakUser("grace@demo.test", "Grace", "Hopper") { Password = "grace-Password1", Roles = ["Administrator", "Editor"] };
        await Reconcile([new("demo-auth", "secret", []), new("demo-grafana", "secret", [])], [ada with { Roles = [] }, grace], ct);

        (await RealmRolesAsync(ada.Email, ct)).ShouldNotContain("Administrator");
        (await RealmRolesAsync(grace.Email, ct)).ShouldBe(["Administrator", "Editor"], ignoreOrder: true);
        using var admin = await AdminAsync(ct);
        foreach (var clientId in new[] { "demo-auth", "demo-grafana" })
        {
            var client = (await admin.GetFromJsonAsync<JsonArray>($"admin/realms/demo/clients?clientId={clientId}", ct))!.Single()!;
            var mappers = await admin.GetFromJsonAsync<JsonArray>($"admin/realms/demo/clients/{client["id"]}/protocol-mappers/models", ct);
            mappers!.ShouldContain(m => m!["config"]!["claim.name"]!.GetValue<string>() == "roles" && m["protocolMapper"]!.GetValue<string>() == "oidc-usermodel-realm-role-mapper");
        }
    }

    [Fact]
    public async Task The_sign_in_page_shows_the_app_name_and_the_login_theme_from_the_mounted_folder()
    {
        var ct = TestContext.Current.CancellationToken;
        KeycloakLoginTheme.Write(Themes, "coworkee-demo", new("Demo App", KeycloakLoginTheme.DefaultLight, KeycloakLoginTheme.DefaultDark)
        {
            Logo = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\"><rect width=\"1\" height=\"1\"/></svg>",
            Design = new() { Layout = "Split" },
        });

        await Reconcile([new("demo-auth", "secret", [])], [], ct, new("Demo App", "coworkee-demo"));

        using var http = new HttpClient { BaseAddress = Base };
        var page = await http.GetStringAsync("realms/demo/protocol/openid-connect/auth?client_id=demo-auth&response_type=code&scope=openid&redirect_uri=http%3A%2F%2Flocalhost%2Fcallback", ct);
        page.ShouldContain("Demo App");
        var css = Regex.Match(page, "href=\"([^\"]+/login/coworkee-demo/css/coworkee.css)\"").Groups[1].Value;
        css.ShouldNotBeEmpty();
        page.ShouldContain("/login/coworkee-demo/js/coworkee.js");
        page.ShouldContain("/login/coworkee-demo/css/styles.css");
        (await http.GetStringAsync(css, ct)).ShouldContain("--cw-primary:#1565c0");
        (await http.GetAsync(css.Replace("css/coworkee.css", "img/logo.svg"), ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await Reconcile([new("demo-auth", "secret", [])], [], ct, new("Renamed", null));
        using var admin = await AdminAsync(ct);
        var realm = await admin.GetFromJsonAsync<JsonObject>("admin/realms/demo", ct);
        realm!["displayName"]!.GetValue<string>().ShouldBe("Renamed");
        realm["loginTheme"]!.GetValue<string>().ShouldBe("coworkee-demo");
    }

    [Fact]
    public async Task Large_localhost_cookies_pass_over_http2()
    {
        using var http = new HttpClient { BaseAddress = Base };
        using var request = new HttpRequestMessage(HttpMethod.Get, "realms/master/.well-known/openid-configuration") { Version = HttpVersion.Version20, VersionPolicy = HttpVersionPolicy.RequestVersionExact };
        request.Headers.Add("Cookie", string.Join("; ", Enumerable.Range(0, 20).Select(i => $"c{i}={new string('x', 1000)}")));

        using var response = await http.SendAsync(request, TestContext.Current.CancellationToken);

        response.Version.ShouldBe(HttpVersion.Version20);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task Reconcile(KeycloakRealm.Client[] clients, KeycloakUser[] users, CancellationToken ct, KeycloakRealm.Look? look = null)
    {
        using var http = new HttpClient { BaseAddress = Base };
        await KeycloakRealm.ReconcileAsync(http, Admin, AdminPassword, "demo", look ?? new("Demo", null), clients, users, ct);
    }

    private async Task<HttpStatusCode> SignInAsync(string user, string password, CancellationToken ct)
    {
        using var http = new HttpClient { BaseAddress = Base };
        using var response = await http.PostAsync("realms/demo/protocol/openid-connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password", ["client_id"] = "admin-cli", ["username"] = user, ["password"] = password,
        }), ct);
        return response.StatusCode;
    }

    private async Task<string[]> RealmRolesAsync(string user, CancellationToken ct)
    {
        using var http = await AdminAsync(ct);
        var id = (await http.GetFromJsonAsync<JsonArray>($"admin/realms/demo/users?exact=true&username={user}", ct))!.Single()!["id"];
        return [.. (await http.GetFromJsonAsync<JsonArray>($"admin/realms/demo/users/{id}/role-mappings/realm", ct))!.Select(r => r!["name"]!.GetValue<string>()).Where(r => !r.StartsWith("default-roles-", StringComparison.Ordinal))];
    }

    private async Task<HttpClient> AdminAsync(CancellationToken ct)
    {
        var http = new HttpClient { BaseAddress = Base };
        using var token = await http.PostAsync("realms/master/protocol/openid-connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password", ["client_id"] = "admin-cli", ["username"] = Admin, ["password"] = AdminPassword,
        }), ct);
        http.DefaultRequestHeaders.Authorization = new("Bearer", (await token.Content.ReadFromJsonAsync<JsonObject>(ct))!["access_token"]!.GetValue<string>());
        return http;
    }

    private async Task<string> SecretAsync(string clientId, CancellationToken ct)
    {
        using var http = await AdminAsync(ct);
        var client = (await http.GetFromJsonAsync<JsonArray>($"admin/realms/demo/clients?clientId={clientId}", ct))!.Single()!;
        return (await http.GetFromJsonAsync<JsonObject>($"admin/realms/demo/clients/{client["id"]}/client-secret", ct))!["value"]!.GetValue<string>();
    }
}
