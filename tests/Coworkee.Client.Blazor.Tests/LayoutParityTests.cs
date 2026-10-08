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
    public async Task Groups_are_tree_nodes_and_the_filter_narrows_them()
    {
        var layout = RenderLayout();
        await OpenNavigationAsync(layout);

        layout.WaitForAssertion(() => layout.FindAll("[data-nav-group='Catalog']").ShouldNotBeEmpty());
        layout.Find("[data-testid='nav-drawer'] input").Input("rol");

        layout.WaitForAssertion(() => layout.FindAll("[data-nav='/catalog/brands']").ShouldBeEmpty(), TimeSpan.FromSeconds(3));
        layout.FindAll("[data-nav='/admin/roles']").ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Pin_and_single_expand_are_remembered()
    {
        var layout = RenderLayout();
        await OpenNavigationAsync(layout);

        await layout.Find("[data-testid='nav-pin'] button, button[data-testid='nav-pin']").ClickAsync(new());
        await layout.Find("[data-testid='nav-single-expand'] button, button[data-testid='nav-single-expand']").ClickAsync(new());

        var stored = JSInterop.Invocations["localStorage.setItem"].Last().Arguments[1]!.ToString()!;
        stored.ShouldContain("\"Pinned\":false");
        stored.ShouldContain("\"SingleExpand\":true");
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
