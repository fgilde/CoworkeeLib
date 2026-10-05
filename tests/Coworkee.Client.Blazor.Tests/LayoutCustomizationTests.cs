using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Customization;
using Coworkee.Client.Blazor.Navigation;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class LayoutCustomizationTests : ClientTestBase
{
    public LayoutCustomizationTests()
    {
        Api.GetSetupStatusAsync(Arg.Any<CancellationToken>()).Returns(new SetupStatusDto(true));
        Services.AddSingleton<INavigationContributor>(new StaticNavigation(
            new CoworkeeNavItem("Products", "/catalog/products", "", Group: "Catalog", Order: 2),
            new CoworkeeNavItem("Brands", "/catalog/brands", "", Group: "Catalog", Order: 1),
            new CoworkeeNavItem("Dashboard", "/dashboard", ""),
            new CoworkeeNavItem("Secret", "/secret", "")));
        AddAuthorization().SetAuthorized("Ada");
    }

    private IRenderedComponent<CoworkeeLayout> RenderLayout() =>
        Render<CoworkeeLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "body"))));

    [Fact]
    public void Navigation_groups_orders_and_hides_items()
    {
        Services.Configure<NavigationMenuOptions>(o => o.Hide("/secret").OrderGroup("Catalog", 1));

        var layout = RenderLayout();

        var catalog = layout.Find("[data-nav-group='Catalog']");
        catalog.InnerHtml.IndexOf("/catalog/brands", StringComparison.Ordinal).ShouldBeLessThan(catalog.InnerHtml.IndexOf("/catalog/products", StringComparison.Ordinal));
        layout.Markup.ShouldContain("/dashboard");
        layout.Markup.ShouldNotContain("/secret");
    }

    [Fact]
    public void App_bar_contributors_add_their_components()
    {
        Services.AddSingleton<IAppBarContributor>(new Bar());

        RenderLayout().Markup.ShouldContain("language-switch");
    }

    [Fact]
    public void Any_part_of_the_layout_can_be_replaced()
    {
        Services.ReplaceComponent<CoworkeeUserMenu, OwnUserMenu>();

        var layout = RenderLayout();

        layout.Markup.ShouldContain("own-user-menu");
        layout.FindAll("[data-testid='user-menu']").ShouldBeEmpty();
    }

    internal sealed class StaticNavigation(params CoworkeeNavItem[] items) : INavigationContributor
    {
        public IEnumerable<CoworkeeNavItem> Items { get; } = items;
    }

    private sealed class Bar : IAppBarContributor
    {
        public IEnumerable<AppBarItem> Items => [new(typeof(LanguageSwitch))];
    }

    public sealed class LanguageSwitch : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddMarkupContent(0, "<span>language-switch</span>");
    }

    public sealed class OwnUserMenu : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddMarkupContent(0, "<span>own-user-menu</span>");
    }
}
