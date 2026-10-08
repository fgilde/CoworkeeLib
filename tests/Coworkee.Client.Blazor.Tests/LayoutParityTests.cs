using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Navigation;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class LayoutParityTests : ClientTestBase
{
    public LayoutParityTests()
    {
        Api.GetSetupStatusAsync(Arg.Any<CancellationToken>()).Returns(new SetupStatusDto(true));
        Services.AddSingleton<INavigationContributor>(new LayoutCustomizationTests.StaticNavigation(
            new CoworkeeNavItem("Products", "/catalog/products", Icons.Material.Outlined.Inventory, Group: "Catalog"),
            new CoworkeeNavItem("Brands", "/catalog/brands", Icons.Material.Outlined.Sell, Group: "Catalog"),
            new CoworkeeNavItem("Roles", "/admin/roles", Icons.Material.Outlined.Shield, Group: "Admin")));
        AddAuthorization().SetAuthorized("Ada");
    }

    private IRenderedComponent<CoworkeeLayout> RenderLayout() =>
        Render<CoworkeeLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "body"))));

    [Fact]
    public void The_filter_narrows_the_navigation_to_matching_entries()
    {
        var layout = RenderLayout();

        layout.Find("input[data-testid='nav-filter'], [data-testid='nav-filter'] input").Input("rol");

        layout.WaitForAssertion(() => layout.Markup.ShouldNotContain("/catalog/brands"));
        layout.Markup.ShouldContain("/admin/roles");
        layout.FindAll("[data-nav-group='Catalog']").ShouldBeEmpty();
    }

    [Fact]
    public async Task Unpinning_turns_the_drawer_into_icons_and_is_remembered()
    {
        var layout = RenderLayout();

        await layout.Find("[data-testid='nav-pin'] button, button[data-testid='nav-pin']").ClickAsync(new());

        JSInterop.Invocations["localStorage.setItem"].ShouldContain(i => i.Arguments[1]!.ToString()!.Contains("\"Pinned\":false"));
        layout.WaitForAssertion(() => layout.FindAll("[data-nav-group]").ShouldBeEmpty());
        layout.Markup.ShouldContain("/catalog/brands");
    }

    [Fact]
    public async Task Single_expand_keeps_only_one_group_open()
    {
        var layout = RenderLayout();

        await layout.Find("[data-testid='nav-single-expand'] button, button[data-testid='nav-single-expand']").ClickAsync(new());

        layout.WaitForAssertion(() => layout.FindAll("[data-nav-group] .mud-collapse-entered").Count.ShouldBeLessThanOrEqualTo(1));
    }

    [Fact]
    public async Task Right_to_left_and_about_come_from_the_app_bar()
    {
        Services.AddSingleton(new CoworkeeClientOptions { AppTitle = "Demo", AppVersion = "1.2.3" });
        var layout = RenderLayout();

        await layout.Find("[data-testid='rtl-toggle']").ClickAsync(new());
        layout.WaitForAssertion(() => layout.Markup.ShouldContain("mud-application-layout-rtl"));

        _ = layout.Find("[data-testid='about-button']").ClickAsync(new());
        layout.WaitForAssertion(() => layout.Find("[data-testid='app-version']").TextContent.ShouldBe("1.2.3"));
    }
}
