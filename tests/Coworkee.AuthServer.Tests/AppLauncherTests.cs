using System.Net;
using Coworkee.AuthServer.Clients;
using Coworkee.Contracts.Identity;
using Coworkee.Core.Results;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.AuthServer.Tests;

public sealed class AppLauncherTests(AuthApp app) : IAsyncLifetime
{
    private const string Admin = "admin@acme.test";
    private const string Password = "Admin#12345";
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_launcher_shows_the_apps_users_sign_in_to_with_logo_description_and_link()
    {
        await SendAsync(new CreateClient(Request("partner", "https://partner.test/") with { LogoUrl = "https://cdn.test/partner.svg", Description = "Partner portal" }));
        await SendAsync(new CreateClient(Request("hidden", "https://hidden.test/") with { ShowInLauncher = false }));
        await SendAsync(new CreateClient(Request("homeless", null)));
        await SendAsync(new CreateClient(new ClientRequest("robot", "Robot", "confidential", "implicit", [], [], [ClientGrantTypes.ClientCredentials], [],
            ClientUri: "https://robot.test/", ShowInLauncher: true)));
        await app.App.Services.GetRequiredService<HybridCache>().RemoveAsync("coworkee:auth-clients", Ct);
        var flow = new OidcFlow(app, AuthApp.ClientId, AuthApp.RedirectUri);
        await flow.LoginAsync(Admin, Password);

        using var response = await flow.Browser.GetAsync("/Account/Apps", Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("href=\"https://client.test/\"");
        html.ShouldContain("The test app");
        html.ShouldContain("href=\"https://partner.test/\"");
        html.ShouldContain("src=\"https://cdn.test/partner.svg\"");
        html.ShouldContain("Partner portal");
        html.ShouldNotContain("hidden.test");
        html.ShouldNotContain("robot.test");
        System.Text.RegularExpressions.Regex.Matches(html, "data-testid=\"app\"").Count.ShouldBe(2);
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("img-src 'self' data: https://cdn.test;");
    }

    [Fact]
    public async Task The_launcher_needs_a_sign_in()
    {
        using var response = await app.Browser().GetAsync("/Account/Apps", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldContain("/Account/Login");
    }

    [Fact]
    public async Task Client_app_details_are_stored_changed_and_come_from_the_configuration()
    {
        var id = (await SendAsync(new CreateClient(Request("partner", "https://partner.test/") with { LogoUrl = "https://cdn.test/p.svg", Description = " Portal " }))).Value.Id;
        var created = await ClientAsync("partner");
        (await SendAsync(new UpdateClient(id, Request("partner", null) with { ShowInLauncher = false }))).IsSuccess.ShouldBeTrue();
        var changed = await ClientAsync("partner");

        created.ShouldBe(created with { ClientUri = "https://partner.test/", LogoUrl = "https://cdn.test/p.svg", Description = "Portal", ShowInLauncher = true });
        (changed.ClientUri, changed.LogoUrl, changed.Description, changed.ShowInLauncher).ShouldBe((null, null, null, false));
        var configured = await ClientAsync(AuthApp.ClientId);
        (configured.ClientUri, configured.Description, configured.ShowInLauncher).ShouldBe(("https://client.test/", "The test app", true));
    }

    [Theory]
    [InlineData("javascript:alert(1)", null)]
    [InlineData(null, "data:image/svg+xml;base64,AAAA")]
    [InlineData("/relative", null)]
    public async Task App_addresses_have_to_be_web_addresses(string? clientUri, string? logoUrl) =>
        (await SendAsync(new CreateClient(Request("partner", clientUri) with { LogoUrl = logoUrl }))).Error!.Kind.ShouldBe(ErrorKind.Validation);

    private async Task<ClientDto> ClientAsync(string clientId) => (await SendAsync(new GetClients())).Value.Single(c => c.ClientId == clientId);

    private static ClientRequest Request(string clientId, string? clientUri) =>
        new(clientId, "Partner", "public", "implicit", ["https://other.test/cb"], [], [ClientGrantTypes.AuthorizationCode], ["openid"],
            ClientUri: clientUri, ShowInLauncher: true);

    private Task<T> SendAsync<T>(Coworkee.Application.Messaging.IRequest<T> request) => OidcFlow.SendAsync(app, _setup.AdminUserId, _setup.TenantId, request);
}
