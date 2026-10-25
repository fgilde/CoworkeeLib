using System.Net;
using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.AuthServer.Tests;

public sealed class SystemAdministratorsOnlyTests(AuthApp app) : IAsyncLifetime
{
    private Coworkee.Contracts.Identity.SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // a tool asks for the user only, not for the api
    private OidcFlow ToolFlow() => new(app, AuthApp.ToolClientId, AuthApp.ToolRedirectUri, "tool-secret") { Scope = "openid profile email" };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_administrator_of_the_system_organisation_signs_in()
    {
        var tokens = await ToolFlow().SignInAsync("admin@acme.test", "Admin#12345");

        tokens.GetProperty("id_token").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Other_users_are_turned_away()
    {
        using (CurrentUserScope.Begin(new ImpersonatedUser(null, null)))
        using (var scope = app.App.Services.CreateScope())
        {
            var user = new User { TenantId = _setup.TenantId, UserName = "eve@acme.test", Email = "eve@acme.test", EmailConfirmed = true };
            (await scope.ServiceProvider.GetRequiredService<UserManager<User>>().CreateAsync(user, "Passw0rd!x")).Succeeded.ShouldBeTrue();
            await scope.ServiceProvider.GetRequiredService<AuthTestDbContext>().SaveChangesAsync(Ct);
        }

        var flow = ToolFlow();
        (await flow.LoginAsync("eve@acme.test", "Passw0rd!x", flow.AuthorizeUrl)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var response = await flow.FollowAsync(await flow.AuthorizeAsync());

        response.Headers.Location!.ToString().ShouldStartWith(AuthApp.ToolRedirectUri);
        response.Headers.Location.Query.ShouldContain("error=access_denied");
        // the app's own client lets the same user in
        (await new OidcFlow(app).SignInAsync("eve@acme.test", "Passw0rd!x")).GetProperty("id_token").GetString().ShouldNotBeNullOrEmpty();
    }
}
