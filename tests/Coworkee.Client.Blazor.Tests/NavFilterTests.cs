using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Coworkee.Client.Blazor.Tests;

public sealed class NavFilterTests : ClientTestBase
{
    public NavFilterTests()
    {
        Services.AddSingleton<INavigationContributor>(new LayoutCustomizationTests.StaticNavigation(
            new CoworkeeNavItem("Roles", "/admin/roles", Icons.Material.Outlined.Shield, Group: "Admin"),
            new CoworkeeNavItem("Users", "/admin/users", Icons.Material.Outlined.Person, Group: "Admin"),
            new CoworkeeNavItem("Tenants", "/admin/tenants", Icons.Material.Outlined.Domain, Group: "Admin")));
    }

    [Fact]
    public async Task The_typed_filter_text_stays_in_the_box_after_navigating()
    {
        AddAuthorization().SetAuthorized("Ada");
        var menu = Render<CoworkeeNavMenu>();
        menu.WaitForAssertion(() => menu.FindAll("[data-nav='/admin/roles']").ShouldNotBeEmpty());

        // one committed chip and the active text behind it
        await menu.Find(CoworkeeNavMenu.FilterInput).InputAsync(new ChangeEventArgs { Value = "ten" });
        await menu.Find(CoworkeeNavMenu.FilterInput).KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        await menu.Find(CoworkeeNavMenu.FilterInput).InputAsync(new ChangeEventArgs { Value = "rol" });
        menu.WaitForAssertion(() => menu.FindAll("[data-nav='/admin/users']").ShouldBeEmpty());

        // clicking a nav item moves the focus out of the filter box
        await menu.Find(CoworkeeNavMenu.FilterInput).BlurAsync(new FocusEventArgs());
        await menu.Find("[data-nav='/admin/roles']").ClickAsync(new MouseEventArgs());
        await menu.InvokeAsync(() => Services.GetRequiredService<NavigationManager>().NavigateTo("/admin/roles"));

        // the box keeps the text after the re-render (MudEx 9.11.4 emptied it)
        menu.WaitForAssertion(() => menu.Find(CoworkeeNavMenu.FilterInput).GetAttribute("value").ShouldBe("rol"));
        menu.FindAll("[data-chip='ten']").Count.ShouldBe(1);
        menu.FindAll("[data-nav='/admin/users']").ShouldBeEmpty();
        menu.FindAll("[data-nav='/admin/roles']").ShouldNotBeEmpty();
    }
}
