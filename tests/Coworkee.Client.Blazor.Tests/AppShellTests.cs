using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Navigation;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

/// <summary>The app's router with every page of the library next to pages apps have of their own, on an install that is not set up yet.</summary>
public sealed class AppShellTests : ClientTestBase
{
    [Fact]
    public void A_fresh_install_reaches_setup_with_the_library_pages_next_to_the_apps_own()
    {
        Api.GetSetupStatusAsync(Arg.Any<CancellationToken>()).Returns(new SetupStatusDto(false));
        AddAuthorization();

        // two pages with one route fail the whole route table, so the app would never get to setup
        Render<Router>(p => p
            .Add(r => r.AppAssembly, typeof(AppShellTests).Assembly)
            .Add(r => r.AdditionalAssemblies, [typeof(CoworkeeLayout).Assembly])
            .Add(r => r.Found, data => builder =>
            {
                builder.OpenComponent<RouteView>(0);
                builder.AddAttribute(1, nameof(RouteView.RouteData), data);
                builder.AddAttribute(2, nameof(RouteView.DefaultLayout), typeof(CoworkeeLayout));
                builder.CloseComponent();
            }));

        Services.GetRequiredService<NavigationManager>().Uri.ShouldEndWith("/setup");
    }

    [Theory]
    [InlineData("/admin/users", "/cw/admin/users", "app users")]
    [InlineData("/cw/admin/users", "/cw/admin/users", "app users")]
    [InlineData("/admin/ai", "/admin/ai", "app ai")]
    [InlineData("/admin/backups", "/admin/backups", "Not found.")]
    [InlineData("/cw/admin/backups", "/cw/admin/backups", "Not found.")]
    [InlineData("/nowhere", "/nowhere", "Not found.")]
    public void Library_pages_live_under_the_prefix_next_to_replaced_turned_off_and_own_pages(string link, string address, string shown)
    {
        var options = new CoworkeeClientOptions();
        options.Pages.Replace<AppUsersPage>("users").Disable("backups");
        Services.AddSingleton(options);
        foreach (var page in LibraryPages.All)
        {
            Services.AddSingleton(page);
        }

        AddAuthorization().SetAuthorized("Ada");
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(link);

        var router = Render<Router>(p => p
            .Add(r => r.AppAssembly, typeof(AppShellTests).Assembly)
            .Add(r => r.AdditionalAssemblies, [typeof(CoworkeeLayout).Assembly])
            .Add(r => r.Found, data => builder =>
            {
                builder.OpenComponent<AuthorizeRouteView>(0);
                builder.AddAttribute(1, nameof(AuthorizeRouteView.RouteData), data);
                builder.CloseComponent();
            })
            .Add(r => r.NotFound, builder => builder.AddContent(0, "not found")));

        router.WaitForAssertion(() => router.Markup.ShouldContain(shown));
        nav.Uri.ShouldEndWith(address);
    }
}

[Route("/")]
public sealed class AppHomePage : ComponentBase;

// routes apps used for their own pages before the library had them
[Route("/admin/ai")]
public sealed class AppAiPage : ComponentBase
{
    protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder) => builder.AddContent(0, "app ai");
}

// an app's own page in place of the library's user list
public sealed class AppUsersPage : ComponentBase
{
    protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder) => builder.AddContent(0, "app users");
}
