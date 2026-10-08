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
    public async Task Navigation_shows_only_permitted_items()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("Ada");
        auth.SetPolicies(Security.PermissionPolicy.For(IdentityPermissions.Users.View));

        var layout = Render<CoworkeeLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "body"))));

        await OpenNavigationAsync(layout);

        layout.WaitForAssertion(() => layout.Markup.ShouldContain("/admin/users"));
        layout.Markup.ShouldNotContain("/admin/roles");
    }

    [Fact]
    public async Task Sign_out_navigates_to_the_identity_provider_end_session_url()
    {
        Api.LogoutAsync(Arg.Any<CancellationToken>()).Returns(new BffLogoutDto("https://auth.test/connect/endsession?x=1"));
        AddAuthorization().SetAuthorized("Ada");
        var layout = Render<CoworkeeLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "body"))));

        await layout.Find("button[aria-label='Account']").ClickAsync(new());
        await layout.WaitForElements(".mud-menu-item").Single(i => i.TextContent.Contains("Sign out")).ClickAsync(new());

        Services.GetRequiredService<NavigationManager>().Uri.ShouldBe("https://auth.test/connect/endsession?x=1");
    }

    [Fact]
    public void Logo_is_rendered_as_an_image_never_as_markup()
    {
        Api.GetCurrentThemeAsync(Arg.Any<CancellationToken>()).Returns(new Coworkee.Contracts.Theming.ThemeDto(
            Guid.CreateVersion7(), "Brand", false, true, System.Text.Json.JsonSerializer.SerializeToElement(new { }), System.Text.Json.JsonSerializer.SerializeToElement(new { }),
            null, null, "<svg xmlns=\"http://www.w3.org/2000/svg\"><circle r=\"4\"/></svg>", null, 1));
        AddAuthorization();

        var layout = Render<CoworkeeLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "body"))));

        layout.WaitForAssertion(() => layout.Find("img[data-testid='logo']").GetAttribute("src")!.ShouldStartWith("data:image/svg+xml;base64,"));
        layout.Markup.ShouldNotContain("<circle");
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
