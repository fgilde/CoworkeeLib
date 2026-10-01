using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class LayoutTests : ClientTestBase
{
    public LayoutTests()
    {
        Services.AddSingleton<INavigationContributor, AdminNavigation>();
        Api.GetSetupStatusAsync(Arg.Any<CancellationToken>()).Returns(new SetupStatusDto(true));
    }

    [Fact]
    public void Navigation_shows_only_permitted_items()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("Ada");
        auth.SetPolicies(Security.PermissionPolicy.For(IdentityPermissions.Users.View));

        var layout = Render<CoworkeeLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "body"))));

        layout.Markup.ShouldContain("/admin/users");
        layout.Markup.ShouldNotContain("/admin/roles");
    }

    [Fact]
    public async Task Sign_out_navigates_to_the_identity_provider_end_session_url()
    {
        Api.LogoutAsync(Arg.Any<CancellationToken>()).Returns(new BffLogoutDto("https://auth.test/connect/endsession?x=1"));
        AddAuthorization().SetAuthorized("Ada");
        var layout = Render<CoworkeeLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "body"))));

        await layout.FindAll("button").Single(b => b.TextContent.Contains("Sign out")).ClickAsync(new());

        Services.GetRequiredService<NavigationManager>().Uri.ShouldBe("https://auth.test/connect/endsession?x=1");
    }

    [Fact]
    public void Uninitialized_system_redirects_to_setup()
    {
        Api.GetSetupStatusAsync(Arg.Any<CancellationToken>()).Returns(new SetupStatusDto(false));
        AddAuthorization();

        Render<CoworkeeLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "body"))));

        Services.GetRequiredService<NavigationManager>().Uri.ShouldEndWith("/setup");
    }
}
